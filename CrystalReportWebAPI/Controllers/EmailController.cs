using CrystalReportWebAPI.Models;
using CrystalReportWebAPI.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Hosting;
using System.Text;

namespace CrystalReportWebAPI.Controllers
{
    [RoutePrefix("api/Email")]
    public class EmailController : ApiController
    {
        private static readonly object _logLock = new object();
        private string _providerName;

        private bool IsOdbc => _providerName == "System.Data.Odbc";

        private void AddParam(DbCommand cmd, string name, object value)
        {
            var param = cmd.CreateParameter();
            if (IsOdbc)
            {
                // Positional parameters for ODBC, name doesn't matter much but it helps for debugging
                // SQL must use '?' instead of '@name'
                param.ParameterName = name; 
                cmd.CommandText = cmd.CommandText.Replace(name, "?");
            }
            else
            {
                param.ParameterName = name;
            }
            param.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }

        private void WriteLog(string logType, string message, string invoiceNumber = null)
        {
            try
            {
                string logsFolder = Path.Combine(HostingEnvironment.MapPath("~/"), "..", "logs");
                if (!Directory.Exists(logsFolder))
                {
                    Directory.CreateDirectory(logsFolder);
                }

                string logFileName = $"{logType}_{DateTime.Now:yyyyMMdd}.log";
                string logFilePath = Path.Combine(logsFolder, logFileName);

                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | Invoice: {invoiceNumber ?? "N/A"} | {message}{Environment.NewLine}";

                lock (_logLock)
                {
                    File.AppendAllText(logFilePath, logEntry);
                }

                // Also write to debug console for immediate visibility
                System.Diagnostics.Debug.WriteLine($"[{logType}] {message}");
            }
            catch (Exception ex)
            {
                // If logging fails, at least try to write to debug console
                System.Diagnostics.Debug.WriteLine($"Logging failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Original message: {message}");
            }
        }
        [AllowAnonymous]
        [Route("ProcessPendingInvoices")]
        [HttpGet]
        public HttpResponseMessage ProcessPendingInvoices()
        {
            WriteLog("INFO", "=== API Execution Started: ProcessPendingInvoices ===");
            try
            {
                var settings = ConfigurationManager.ConnectionStrings["DefaultConnection"];
                string connectionString = settings.ConnectionString;
                _providerName = settings.ProviderName;
                DbProviderFactory factory = DbProviderFactories.GetFactory(_providerName);

                using (DbConnection conn = factory.CreateConnection())
                {
                    conn.ConnectionString = connectionString;
                    conn.Open();

                    // Check if there are pending emails to process
                    string checkQuery = "SELECT COUNT(*) FROM VALIDATED_LOCAL_INV_PENDING_EMAIL";
                    DbCommand checkCmd = conn.CreateCommand();
                    checkCmd.CommandText = checkQuery;
                    int pendingCount = Convert.ToInt32(checkCmd.ExecuteScalar());
                    WriteLog("INFO", $"Found {pendingCount} pending emails to process.");

                    if (pendingCount == 0)
                    {
                        WriteLog("INFO", "=== API Execution Ended: No pending emails ===");
                        return Request.CreateResponse(HttpStatusCode.OK, new
                        {
                            Success = true,
                            Message = "No pending emails to process",
                            ProcessedCount = 0
                        });
                    }

                    // Get pending invoices first, then close the reader before processing (Limit to 10)
                    List<string> pendingInvoices = new List<string>();
                    string selectQuery = "SELECT TOP 10 invoice FROM VALIDATED_LOCAL_INV_PENDING_EMAIL";

                    using (DbCommand selectCmd = conn.CreateCommand())
                    {
                        selectCmd.CommandText = selectQuery;
                        using (DbDataReader reader = selectCmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                pendingInvoices.Add(Convert.ToString(reader["invoice"]));
                            }
                        }
                    }

                    WriteLog("INFO", $"Data successfully retrieved for pending invoices: {string.Join(", ", pendingInvoices)}");

                    // Now process each invoice with the reader closed
                    List<string> processedInvoices = new List<string>();
                    List<string> failedInvoices = new List<string>();

                    foreach (string invoiceNumber in pendingInvoices)
                    {
                        try
                        {
                            string trimmedInvoiceNumber = invoiceNumber?.Trim();
                            WriteLog("INFO", $"Starting processing loop for invoice: {trimmedInvoiceNumber}", trimmedInvoiceNumber);
                            
                            // CRITICAL: Open a NEW connection for each invoice to prevent connection state issues 
                            // from stopping the entire batch if one fails.
                            using (DbConnection invoiceConn = factory.CreateConnection())
                            {
                                invoiceConn.ConnectionString = connectionString;
                                invoiceConn.Open();
                                ProcessInvoiceEmail(invoiceConn, trimmedInvoiceNumber);
                            }
                            
                            processedInvoices.Add(trimmedInvoiceNumber);
                        }
                        catch (Exception ex)
                        {
                            failedInvoices.Add($"{invoiceNumber}: {ex.Message}");
                            WriteLog("ERROR", $"Failed to process invoice {invoiceNumber}: {ex.Message}", invoiceNumber);
                        }
                        finally
                        {
                            // CRITICAL: Force cleanup of Crystal Report COM objects to prevent "Print Job Limit" exhaustion
                            try 
                            {
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                            }
                            catch { /* Ignore GC errors */ }
                        }
                    }

                    return Request.CreateResponse(HttpStatusCode.OK, new
                    {
                        Success = true,
                        Message = $"Processed {processedInvoices.Count} invoices successfully",
                        ProcessedCount = processedInvoices.Count,
                        FailedCount = failedInvoices.Count,
                        ProcessedInvoices = processedInvoices,
                        FailedInvoices = failedInvoices
                    });
                }
            }
            catch (Exception ex)
            {
                // LogEmailProcess("ProcessPendingInvoices", "Failed", ex.Message);
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private void ProcessInvoiceEmail(DbConnection conn, string invoiceNumber)
        {
            string buyerCode = "";
            DateTime invoiceDate = DateTime.MinValue;
            string contractNumber = "";
            string fieldBuyerEmail = "";
            string fieldBuyerEmailCC = "";
            string pdfFolder = "";
            string bodyTemplate = "";
            string ccRecipients = "";
            string buyerName = "";
            string emailFieldName = "";
            string emailCCFieldName = "";

            // Combined query to get all data in one go
            string combinedQuery = @"
                SELECT
                    i.accode,
                    i.date,
                    i.smf,
                    p.EMAIL_SERVER_FLD_RECIPIENT,
                    p.EMAIL_SERVER_FLD_RECIPIENT_CC,
                    p.PDF,
                    p.INVOICE_BODY_MSG,
                    p.INVOICE_EMAIL,
                    a.NAME as BuyerName
                FROM INVOICE i
                CROSS JOIN (SELECT TOP 1 * FROM PROFILE) p
                LEFT JOIN ACCODE a ON a.CODE = i.accode
                WHERE i.INVOICE = @InvoiceNumber";

            using (DbCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = combinedQuery;
                AddParam(cmd, "@InvoiceNumber", invoiceNumber);

                using (DbDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        throw new Exception($"Invoice {invoiceNumber} not found");
                    }

                    buyerCode = Convert.ToString(reader["accode"]).Trim();
                    invoiceDate = reader["date"] != DBNull.Value ? (DateTime)reader["date"] : DateTime.MinValue;
                    contractNumber = Convert.ToString(reader["smf"]).Trim();
                    emailFieldName = Convert.ToString(reader["EMAIL_SERVER_FLD_RECIPIENT"]).Trim();
                    emailCCFieldName = Convert.ToString(reader["EMAIL_SERVER_FLD_RECIPIENT_CC"]).Trim();
                    pdfFolder = Convert.ToString(reader["PDF"]).Trim();
                    bodyTemplate = Convert.ToString(reader["INVOICE_BODY_MSG"]);
                    ccRecipients = Convert.ToString(reader["INVOICE_EMAIL"]).Trim();
                    buyerName = Convert.ToString(reader["BuyerName"]).Trim();

                    if (string.IsNullOrEmpty(buyerName))
                    {
                        throw new Exception($"Buyer {buyerCode} not found");
                    }
                }
            }
            WriteLog("INFO", "Invoice data and buyer details successfully retrieved.", invoiceNumber);

            // Get actual email addresses from ACCODE table using the field names
            if (!string.IsNullOrEmpty(emailFieldName) || !string.IsNullOrEmpty(emailCCFieldName))
            {
                string emailQuery = "SELECT ";
                List<string> selectFields = new List<string>();

                if (!string.IsNullOrEmpty(emailFieldName))
                {
                    selectFields.Add($"[{emailFieldName}] as RecipientEmail");
                }
                if (!string.IsNullOrEmpty(emailCCFieldName))
                {
                    selectFields.Add($"[{emailCCFieldName}] as CCEmail");
                }

                if (selectFields.Count > 0)
                {
                    emailQuery += string.Join(", ", selectFields) + " FROM ACCODE WHERE CODE = @BuyerCode";

                    using (DbCommand emailCmd = conn.CreateCommand())
                    {
                        emailCmd.CommandText = emailQuery;
                        AddParam(emailCmd, "@BuyerCode", buyerCode);

                        using (DbDataReader emailReader = emailCmd.ExecuteReader())
                        {
                            if (emailReader.Read())
                            {
                                if (!string.IsNullOrEmpty(emailFieldName))
                                {
                                    fieldBuyerEmail = Convert.ToString(emailReader["RecipientEmail"]).Trim();
                                }
                                if (!string.IsNullOrEmpty(emailCCFieldName))
                                {
                                    fieldBuyerEmailCC = Convert.ToString(emailReader["CCEmail"]).Trim();
                                }
                            }
                        }
                    }
                }
            }

            // Generate PDF
            WriteLog("INFO", "Attempting to generate PDF...", invoiceNumber);
            string pdfPath;
            try
            {
                pdfPath = GenerateAndSavePdf(invoiceNumber, invoiceDate, buyerName, pdfFolder);
                WriteLog("INFO", "PDF generated and saved successfully.", invoiceNumber);
            }
            catch (Exception ex)
            {
                WriteLog("ERROR", $"PDF generation failed: {ex.Message}", invoiceNumber);
                throw;
            }

            // Prepare email content
            string subject = $"E-INVOICE {buyerName} - DELIVERY DATED {invoiceDate:dd/MM/yy}";
            string body = PrepareEmailBody(bodyTemplate, invoiceNumber, invoiceDate, buyerCode, conn);

            // Combine CC recipients
            string combinedCC = fieldBuyerEmailCC ?? "";
            if (!string.IsNullOrEmpty(ccRecipients) && !string.IsNullOrEmpty(combinedCC))
            {
                combinedCC += ";" + ccRecipients;
            }
            else if (!string.IsNullOrEmpty(ccRecipients))
            {
                combinedCC = ccRecipients;
            }

            // Debug logging for insert operation
            WriteLog("DEBUG", $"=== INSERT DEBUG LOG ===", invoiceNumber);
            WriteLog("DEBUG", $"Sender: '{fieldBuyerEmail}' (Length: {fieldBuyerEmail?.Length ?? 0})", invoiceNumber);
            WriteLog("DEBUG", $"CC: '{combinedCC}' (Length: {combinedCC?.Length ?? 0})", invoiceNumber);
            WriteLog("DEBUG", $"Subject: '{subject}' (Length: {subject?.Length ?? 0})", invoiceNumber);
            WriteLog("DEBUG", $"Body Length: {body?.Length ?? 0}", invoiceNumber);
            WriteLog("DEBUG", $"Attach1: '{pdfPath}' (Length: {pdfPath?.Length ?? 0})", invoiceNumber);
            WriteLog("DEBUG", $"=== END DEBUG LOG ===", invoiceNumber);

            // Insert into LogMail_Inv with all required fields and get EmailKey using OUTPUT
            string insertQuery = @"
                INSERT INTO LogMail_Inv (
                    SENDER,
                    CC,
                    SUBJECT,
                    BODY,
                    ATTACH1,
                    ATTACH2,
                    ATTACH3,
                    STATUS,
                    DATE
                )
                OUTPUT INSERTED.pkkey
                VALUES (
                    @Sender,
                    @CC,
                    @Subject,
                    @Body,
                    @Attach1,
                    NULL,
                    NULL,
                    0,
                    GETDATE()
                )";

            using (DbCommand insertCmd = conn.CreateCommand())
            {
                insertCmd.CommandText = insertQuery;
                
                // CRITICAL: Order must match the query for ODBC positional parameters
                AddParam(insertCmd, "@Sender", fieldBuyerEmail ?? "");
                AddParam(insertCmd, "@CC", combinedCC);
                AddParam(insertCmd, "@Subject", subject ?? "");
                AddParam(insertCmd, "@Body", body ?? "");
                AddParam(insertCmd, "@Attach1", pdfPath ?? "");

                object identityResult = insertCmd.ExecuteScalar();
                if (identityResult == null || identityResult == DBNull.Value)
                {
                    throw new Exception("Failed to get EmailKey after insert");
                }
                int EmailKey = Convert.ToInt32(identityResult);

                // Send email using SmtpClient
                WriteLog("INFO", "Ready to send email via SMTP...", invoiceNumber);
                SendEmailInternal(EmailKey, conn);
                WriteLog("INFO", "Email sent successfully via internal helper.", invoiceNumber);

                // Update invoice
                string updateQuery = @"
                UPDATE INVOICE
                SET EMAILSENT_USER = 'API',
                    EMAILSENT_DATE = GETDATE(),
                    EMAILSENT = 1
                WHERE INVOICE = @InvoiceNumber";

                using (DbCommand updateCmd = conn.CreateCommand())
                {
                    updateCmd.CommandText = updateQuery;
                    AddParam(updateCmd, "@InvoiceNumber", invoiceNumber);
                    updateCmd.ExecuteNonQuery();
                    WriteLog("INFO", "Database updated successfully (EMAILSENT=1). Process complete for this invoice.", invoiceNumber);
                }
            }
        }

        private string GenerateAndSavePdf(string invoiceNumber, DateTime invoiceDate, string buyerName, string pdfFolder)
        {
            try
            {
                // Generate PDF using existing Crystal Report logic
                string reportPath = "~/Reports/Mewah";
                string reportFileName = "TaxInvoice_SalesLocal_EINV.rpt";
                string recordSelectionFormula = $"{{INVOICE.INVOICE}} = '{invoiceNumber}'";

                // Create PDF directory structure (same logic as @PdfPath from stored procedure)
                string dateStr = invoiceDate.ToString("yyyyMMdd");
                string trimmedInvoiceNumber = invoiceNumber?.Trim();
                string trimmedBuyerName = buyerName?.Trim();
                string trimmedPdfFolder = pdfFolder?.Trim();

                string pdfDirectory = Path.Combine(trimmedPdfFolder, "INVPDF", $"{dateStr}_{trimmedInvoiceNumber}_{trimmedBuyerName}");

                if (!Directory.Exists(pdfDirectory))
                {
                    Directory.CreateDirectory(pdfDirectory);
                }

                // Generate filename (same logic as stored procedure)
                string fileName = $"{trimmedInvoiceNumber}_{dateStr}_{trimmedBuyerName}.pdf";
                string pdfPath = Path.Combine(pdfDirectory, fileName);

                // Generate and save PDF directly to file system
                // Pass a logging action that writes with specific prefix to existing log
                CrystalReport.SaveReportToFile(reportPath, reportFileName, pdfPath, null, recordSelectionFormula, 
                    (msg) => WriteLog("CRYSTAL", msg, invoiceNumber));

                return pdfPath;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to generate PDF: {ex.Message}");
            }
        }

        private string PrepareEmailBody(string bodyTemplate, string invoiceNumber, DateTime invoiceDate, string buyerCode, DbConnection conn)
        {
            string body = bodyTemplate;

            // Replace placeholders
            body = body.Replace("@INVOICE", invoiceNumber);
            body = body.Replace("@INVDATE", invoiceDate.ToString("dd/MM/yyyy"));

            // Get contact information and sender details in one query
            string combinedQuery = @"
                SELECT
                    (SELECT TOP 1 CONTACT1 FROM ACCODE WHERE CODE = @BuyerCode) as Contact,
                    (SELECT TOP 1 SENDER_DETAIL FROM acc_sign WHERE acc_sign = '') as SenderDetail";

            using (DbCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = combinedQuery;
                AddParam(cmd, "@BuyerCode", buyerCode);

                using (DbDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        string contact = Convert.ToString(reader["Contact"]);
                        string senderDetail = Convert.ToString(reader["SenderDetail"]);

                        body = body.Replace("@CONTACT", contact);

                        if (!string.IsNullOrEmpty(senderDetail))
                        {
                            body += senderDetail;
                        }
                    }
                }
            }

            return body;
        }

        private void WriteMailErrorLog(string message)
        {
            try
            {
                string logFolder = @"C:\Temp\Emaillogs";
                if (!Directory.Exists(logFolder))
                {
                    Directory.CreateDirectory(logFolder);
                }

                string logFileName = $"mail_error_{DateTime.Now:yyyyMMdd}.log";
                string logFilePath = Path.Combine(logFolder, logFileName);

                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";

                lock (_logLock)
                {
                    File.AppendAllText(logFilePath, logEntry);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logging to mail_error.log failed: {ex.Message}");
            }
        }

        private void SendEmailInternal(int EmailKey, DbConnection conn)
        {
            string sender = "";
            string recipient = "";
            string recipientCC = "";
            string subject = "";
            string body = "";
            List<string> attachmentPaths = new List<string>();

            // Get sender from profile
            string senderQuery = "SELECT TOP 1 EMAIL_SERVER_GRP_EMAIL FROM PROFILE";
            using (DbCommand senderCmd = conn.CreateCommand())
            {
                senderCmd.CommandText = senderQuery;
                object result = senderCmd.ExecuteScalar();
                if (result != DBNull.Value)
                {
                    sender = Convert.ToString(result).Trim();
                }
            }

            // Retrieve email details from LogMail_Inv
            string selectQuery = "SELECT SENDER, CC, SUBJECT, BODY, ATTACH1, ATTACH2, ATTACH3 FROM LogMail_Inv WHERE pkkey = @EmailKey";
            using (DbCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = selectQuery;
                AddParam(cmd, "@EmailKey", EmailKey);

                using (DbDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        recipient = Convert.ToString(reader["SENDER"]).Trim();
                        recipientCC = Convert.ToString(reader["CC"]).Trim();
                        subject = Convert.ToString(reader["SUBJECT"]).Trim();
                        body = Convert.ToString(reader["BODY"]).Trim();

                        // Build attachment list
                        for (int i = 1; i <= 3; i++)
                        {
                            string path = Convert.ToString(reader["ATTACH" + i]).Trim();
                            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            {
                                attachmentPaths.Add(path);
                            }
                        }
                    }
                }
            }

            try
            {
                WriteMailErrorLog("========================================");
                WriteMailErrorLog("Starting email process...");
                WriteMailErrorLog($"FROM address (EMAIL_SERVER_GRP_EMAIL): '{sender}'");

                WriteLog("INFO", "Preparing to send email directly via C# SmtpClient...", EmailKey.ToString());

                using (var mail = new System.Net.Mail.MailMessage())
                {
                    if (string.IsNullOrWhiteSpace(sender))
                    {
                        throw new Exception("FROM address (EMAIL_SERVER_GRP_EMAIL in PROFILE) is empty. Cannot send email.");
                    }
                    mail.From = new System.Net.Mail.MailAddress(sender);

                    // TO RECIPIENTS
                    WriteMailErrorLog("Processing TO recipients...");
                    var toEmails = recipient.Split(';');
                    foreach (var email in toEmails)
                    {
                        var trimmedEmail = email.Trim();
                        if (!string.IsNullOrEmpty(trimmedEmail))
                        {
                            mail.To.Add(new System.Net.Mail.MailAddress(trimmedEmail));
                            WriteMailErrorLog($"Added TO recipient: {trimmedEmail}");
                        }
                    }

                    // CC RECIPIENTS (deduplicated, case-insensitive)
                    if (!string.IsNullOrEmpty(recipientCC))
                    {
                        WriteMailErrorLog("Processing CC recipients...");
                        var addedCC = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var ccEmails = recipientCC.Split(';');
                        foreach (var cc in ccEmails)
                        {
                            var trimmedCC = cc.Trim();
                            if (!string.IsNullOrEmpty(trimmedCC) && addedCC.Add(trimmedCC))
                            {
                                mail.CC.Add(new System.Net.Mail.MailAddress(trimmedCC));
                                WriteMailErrorLog($"Added CC recipient: {trimmedCC}");
                            }
                            else if (!string.IsNullOrEmpty(trimmedCC))
                            {
                                WriteMailErrorLog($"Skipped duplicate CC recipient: {trimmedCC}");
                            }
                        }
                    }

                    mail.Subject = subject;

                    if (!string.IsNullOrEmpty(body))
                    {
                        WriteMailErrorLog("Converting newlines in email body to HTML line breaks (<br />)...");
                        mail.Body = body.Replace("\r\n", "<br />").Replace("\n", "<br />");
                    }
                    else
                    {
                        mail.Body = body;
                    }
                    mail.IsBodyHtml = true;

                    WriteMailErrorLog("Email subject and body assigned.");

                    // ATTACHMENTS
                    if (attachmentPaths.Count > 0)
                    {
                        WriteMailErrorLog("Processing attachments...");
                        foreach (var path in attachmentPaths)
                        {
                            if (File.Exists(path))
                            {
                                mail.Attachments.Add(new System.Net.Mail.Attachment(path));
                                WriteMailErrorLog($"Attachment added: {path}");
                            }
                            else
                            {
                                WriteMailErrorLog($"Attachment file not found: {path}");
                            }
                        }
                    }

                    WriteMailErrorLog("Creating SMTP client...");
                    using (var smtp = new System.Net.Mail.SmtpClient("192.168.2.14", 25))
                    {
                        smtp.Timeout = 15000; // 15 seconds
                        WriteMailErrorLog($"Sending email via 192.168.2.14:25 to {recipient}");
                        smtp.Send(mail);
                    }
                }

                WriteMailErrorLog($"Email sent successfully to {recipient}");
                WriteLog("INFO", $"Email sent successfully via C# SmtpClient to {recipient}", EmailKey.ToString());

                // Update status and sent date
                string updateStr = $"UPDATE LogMail_Inv SET status = 1, sent = GETDATE() WHERE pkkey = {EmailKey}";
                using (DbCommand updateCmd = conn.CreateCommand())
                {
                    updateCmd.CommandText = updateStr;
                    updateCmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"Error sending email: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMsg += $" | Inner Error: {ex.InnerException.Message}";
                }
                WriteMailErrorLog(errorMsg);
                WriteLog("ERROR", $"Failed to send email via SMTP: {errorMsg}", EmailKey.ToString());
                throw new Exception(errorMsg, ex);
            }
            finally
            {
                WriteMailErrorLog("Cleaning up resources...");
                WriteMailErrorLog("Email process completed.");
                WriteMailErrorLog("========================================");
            }
        }

        /*
        private void LogEmailProcess(string processName, string status, string message, string invoiceNumber = null)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
                {
                    conn.Open();

                    string logQuery = @"
                        INSERT INTO EmailJobLog (
                            ProcessName,
                            Status,
                            Message,
                            CreatedDate,
                            InvoiceNumber
                        )
                        VALUES (
                            @ProcessName,
                            @Status,
                            @Message,
                            GETDATE(),
                            @InvoiceNumber
                        )";

                    SqlCommand logCmd = new SqlCommand(logQuery, conn);
                    logCmd.Parameters.AddWithValue("@ProcessName", processName);
                    logCmd.Parameters.AddWithValue("@Status", status);
                    logCmd.Parameters.AddWithValue("@Message", message);
                    logCmd.Parameters.AddWithValue("@InvoiceNumber", invoiceNumber ?? (object)DBNull.Value);

                    logCmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                // Log to system if database logging fails
                System.Diagnostics.Debug.WriteLine($"Email logging failed: {ex.Message}");
            }
        }
        */
    }
}
