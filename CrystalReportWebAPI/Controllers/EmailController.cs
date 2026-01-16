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

                    if (pendingCount == 0)
                    {
                        return Request.CreateResponse(HttpStatusCode.OK, new
                        {
                            Success = true,
                            Message = "No pending emails to process",
                            ProcessedCount = 0
                        });
                    }

                    // Get pending invoices first, then close the reader before processing
                    List<string> pendingInvoices = new List<string>();
                    string selectQuery = "SELECT invoice FROM VALIDATED_LOCAL_INV_PENDING_EMAIL";

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

                    // Now process each invoice with the reader closed
                    List<string> processedInvoices = new List<string>();
                    List<string> failedInvoices = new List<string>();

                    foreach (string invoiceNumber in pendingInvoices)
                    {
                                try
                                {
                                    ProcessInvoiceEmail(conn, invoiceNumber);
                                    processedInvoices.Add(invoiceNumber);
                                }
                                catch (Exception ex)
                                {
                                    failedInvoices.Add($"{invoiceNumber}: {ex.Message}");
                                    // LogEmailProcess("ProcessPendingInvoices", "Error", $"Failed to process invoice {invoiceNumber}: {ex.Message}", invoiceNumber);
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

                    buyerCode = Convert.ToString(reader["accode"]);
                    invoiceDate = reader["date"] != DBNull.Value ? (DateTime)reader["date"] : DateTime.MinValue;
                    contractNumber = Convert.ToString(reader["smf"]);
                    emailFieldName = Convert.ToString(reader["EMAIL_SERVER_FLD_RECIPIENT"]);
                    emailCCFieldName = Convert.ToString(reader["EMAIL_SERVER_FLD_RECIPIENT_CC"]);
                    pdfFolder = Convert.ToString(reader["PDF"]);
                    bodyTemplate = Convert.ToString(reader["INVOICE_BODY_MSG"]);
                    ccRecipients = Convert.ToString(reader["INVOICE_EMAIL"]);
                    buyerName = Convert.ToString(reader["BuyerName"]);

                    if (string.IsNullOrEmpty(buyerName))
                    {
                        throw new Exception($"Buyer {buyerCode} not found");
                    }
                }
            }

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
                                    fieldBuyerEmail = Convert.ToString(emailReader["RecipientEmail"]);
                                }
                                if (!string.IsNullOrEmpty(emailCCFieldName))
                                {
                                    fieldBuyerEmailCC = Convert.ToString(emailReader["CCEmail"]);
                                }
                            }
                        }
                    }
                }
            }

            // Generate PDF
            string pdfPath = GenerateAndSavePdf(invoiceNumber, invoiceDate, buyerName, pdfFolder);

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

                // Send email using PowerShell
                PowerShellSendMail(EmailKey, conn);

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
                string pdfDirectory = Path.Combine(pdfFolder, "INVPDF", $"{dateStr}_{invoiceNumber}_{buyerName}");

                if (!Directory.Exists(pdfDirectory))
                {
                    Directory.CreateDirectory(pdfDirectory);
                }

                // Generate filename (same logic as stored procedure)
                string fileName = $"{invoiceNumber}_{dateStr}_{buyerName}.pdf";
                string pdfPath = Path.Combine(pdfDirectory, fileName);

                // Generate and save PDF directly to file system
                CrystalReport.SaveReportToFile(reportPath, reportFileName, pdfPath, null, recordSelectionFormula);

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

        private void PowerShellSendMail(int EmailKey, DbConnection conn)
        {
            string sender = "";
            string recipient = "";
            string recipientCC = "";
            string subject = "";
            string body = "";
            string attach = "";

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

                        // Build attach string
                        for (int i = 1; i <= 3; i++)
                        {
                            string path = Convert.ToString(reader["ATTACH" + i]).Trim();
                            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            {
                                if (!string.IsNullOrEmpty(attach))
                                {
                                    attach += ";";
                                }
                                attach += path;
                            }
                        }
                    }
                }
            }

            // Prepare PowerShell command
            string scriptPath = Path.Combine(System.Web.Hosting.HostingEnvironment.MapPath("~/"), "..", "EmailSender.ps1");
            string arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -EmailSender \"{sender}\" -Recipients \"{recipient}\" -CCRecipients \"{recipientCC}\" -Subject \"{subject}\" -Body \"{body}\" -Attach \"{attach}\"";

            // Log PowerShell execution attempt
            WriteLog("POWERSHELL", $"Attempting to execute: powershell.exe {arguments}", EmailKey.ToString());

            // Execute PowerShell script
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                try
                {
                    process.Start();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        WriteLog("POWERSHELL", $"PowerShell script executed successfully. Exit code: {process.ExitCode}", EmailKey.ToString());
                        // Update status and sent date
                        string updateStr = $"UPDATE LogMail_Inv SET status = 1, sent = GETDATE() WHERE pkkey = {EmailKey}";
                        using (DbCommand updateCmd = conn.CreateCommand())
                        {
                            updateCmd.CommandText = updateStr;
                            updateCmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        // Log error if needed
                        string error = process.StandardError.ReadToEnd();
                        WriteLog("ERROR", $"PowerShell script failed. Exit code: {process.ExitCode}. Error: {error}", EmailKey.ToString());
                    }
                }
                catch (Exception ex)
                {
                    WriteLog("ERROR", $"Failed to execute PowerShell script: {ex.Message}", EmailKey.ToString());
                }
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
