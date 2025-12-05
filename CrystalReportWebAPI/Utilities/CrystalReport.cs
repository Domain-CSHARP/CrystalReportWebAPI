using CrystalDecisions.CrystalReports.Engine;
using CrystalDecisions.Shared;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Net;
using System.Net.Http;

namespace CrystalReportWebAPI.Utilities
{
    public static class CrystalReport
    {
        public static HttpResponseMessage RenderReport(string reportPath, string reportFileName, string exportFilename)
        {
            return RenderReport(reportPath, reportFileName, exportFilename, null);
        }

        public static HttpResponseMessage RenderReport(
            string reportPath,
            string reportFileName,
            string exportFilename,
            Dictionary<string, object> parameters,
            string recordSelectionFormula = null)
        {
            var rd = new ReportDocument();

            rd.Load(Path.Combine(System.Web.Hosting.HostingEnvironment.MapPath(reportPath), reportFileName));

            // Set database connection
            GetConnectionInfo("DefaultConnection", out string server, out string database, out string userId, out string password, out bool integratedSecurity, out string providerName, out string fullConnectionString);

            // Debug: Log current Windows identity for troubleshooting
            string currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            System.Diagnostics.Debug.WriteLine($"CrystalReport: Current Windows identity: {currentUser}");
            System.Diagnostics.Debug.WriteLine($"CrystalReport: Provider: {providerName}");
            System.Diagnostics.Debug.WriteLine($"CrystalReport: Full Connection String: {fullConnectionString}");
            System.Diagnostics.Debug.WriteLine($"CrystalReport: Using Integrated Security: {integratedSecurity}");
            System.Diagnostics.Debug.WriteLine($"CrystalReport: Server: {server}, Database: {database}");

            // Main report tables
foreach (Table table in rd.Database.Tables)
{
    var logonInfo = table.LogOnInfo;
    if (string.Equals(providerName, "System.Data.Odbc", StringComparison.OrdinalIgnoreCase))
    {
        logonInfo.ConnectionInfo.ServerName = fullConnectionString;
    }
    else
    {
        logonInfo.ConnectionInfo.ServerName = server;
    }
    logonInfo.ConnectionInfo.DatabaseName = database;
    if (integratedSecurity)
    {
        logonInfo.ConnectionInfo.IntegratedSecurity = true;
    }
    else
    {
        logonInfo.ConnectionInfo.UserID = userId;
        logonInfo.ConnectionInfo.Password = password ?? string.Empty;
        logonInfo.ConnectionInfo.IntegratedSecurity = false;
    }
    table.ApplyLogOnInfo(logonInfo);

    // Force refresh of location
    table.Location = table.Location;
}

// Subreport tables
foreach (ReportDocument subreport in rd.Subreports)
{
    foreach (Table table in subreport.Database.Tables)
    {
        var logonInfo = table.LogOnInfo;
        if (string.Equals(providerName, "System.Data.Odbc", StringComparison.OrdinalIgnoreCase))
        {
            logonInfo.ConnectionInfo.ServerName = fullConnectionString;
        }
        else
        {
            logonInfo.ConnectionInfo.ServerName = server;
        }
        logonInfo.ConnectionInfo.DatabaseName = database;
        if (integratedSecurity)
        {
            logonInfo.ConnectionInfo.IntegratedSecurity = true;
        }
        else
        {
            logonInfo.ConnectionInfo.UserID = userId;
            logonInfo.ConnectionInfo.Password = password ?? string.Empty;
            logonInfo.ConnectionInfo.IntegratedSecurity = false;
        }
        table.ApplyLogOnInfo(logonInfo);

        // Force refresh of location
        table.Location = table.Location;
    }
}

            if (parameters == null)
                parameters = new Dictionary<string, object>();

            // Set default values for unlinked parameters only
            foreach (ParameterField param in rd.ParameterFields)
            {
                // Skip subreport-linked parameters (ReportName is filled for subreports)
                if (string.IsNullOrEmpty(param.ReportName) && !parameters.ContainsKey(param.Name))
                {
                    if (param.Name.Equals("Letter head", StringComparison.OrdinalIgnoreCase))
                    {
                        parameters[param.Name] = "True";
                    }
                    else if (param.ParameterValueType == ParameterValueKind.StringParameter)
                        parameters[param.Name] = "";
                    else if (param.ParameterValueType == ParameterValueKind.NumberParameter)
                        parameters[param.Name] = 0;
                    else if (param.ParameterValueType == ParameterValueKind.DateParameter)
                        parameters[param.Name] = DateTime.Now;
                    else if (param.ParameterValueType == ParameterValueKind.BooleanParameter)
                        parameters[param.Name] = false;
                }
            }

            // Apply parameters
            foreach (var param in parameters)
            {
                rd.SetParameterValue(param.Key, param.Value);
            }

            if (!string.IsNullOrEmpty(recordSelectionFormula))
            {
                rd.RecordSelectionFormula = recordSelectionFormula;
            }

            MemoryStream ms = new MemoryStream();
            using (var stream = rd.ExportToStream(ExportFormatType.PortableDocFormat))
            {
                stream.CopyTo(ms);
            }

            var result = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(ms.ToArray())
            };
            result.Content.Headers.ContentDisposition =
                new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
                {
                    FileName = exportFilename
                };
            result.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            return result;
        }

        public static void SaveReportToFile(
            string reportPath,
            string reportFileName,
            string outputFilePath,
            Dictionary<string, object> parameters = null,
            string recordSelectionFormula = null)
        {
            var rd = new ReportDocument();

            rd.Load(Path.Combine(System.Web.Hosting.HostingEnvironment.MapPath(reportPath), reportFileName));

            // Set database connection
            GetConnectionInfo("DefaultConnection", out string server, out string database, out string userId, out string password, out bool integratedSecurity, out string providerName, out string fullConnectionString);

            // Main report tables
            foreach (Table table in rd.Database.Tables)
            {
                var logonInfo = table.LogOnInfo;
                if (string.Equals(providerName, "System.Data.Odbc", StringComparison.OrdinalIgnoreCase))
                {
                    logonInfo.ConnectionInfo.ServerName = fullConnectionString;
                }
                else
                {
                    logonInfo.ConnectionInfo.ServerName = server;
                }
                logonInfo.ConnectionInfo.DatabaseName = database;
                if (integratedSecurity)
                {
                    logonInfo.ConnectionInfo.IntegratedSecurity = true;
                }
                else
                {
                    logonInfo.ConnectionInfo.UserID = userId;
                    logonInfo.ConnectionInfo.Password = password ?? string.Empty;
                    logonInfo.ConnectionInfo.IntegratedSecurity = false;
                }
                table.ApplyLogOnInfo(logonInfo);

                // Force refresh of location
                table.Location = table.Location;
            }

            // Subreport tables
            foreach (ReportDocument subreport in rd.Subreports)
            {
                foreach (Table table in subreport.Database.Tables)
                {
                    var logonInfo = table.LogOnInfo;
                    if (string.Equals(providerName, "System.Data.Odbc", StringComparison.OrdinalIgnoreCase))
                    {
                        logonInfo.ConnectionInfo.ServerName = fullConnectionString;
                    }
                    else
                    {
                        logonInfo.ConnectionInfo.ServerName = server;
                    }
                    logonInfo.ConnectionInfo.DatabaseName = database;
                    if (integratedSecurity)
                    {
                        logonInfo.ConnectionInfo.IntegratedSecurity = true;
                    }
                    else
                    {
                        logonInfo.ConnectionInfo.UserID = userId;
                        logonInfo.ConnectionInfo.Password = password ?? string.Empty;
                        logonInfo.ConnectionInfo.IntegratedSecurity = false;
                    }
                    table.ApplyLogOnInfo(logonInfo);

                    // Force refresh of location
                    table.Location = table.Location;
                }
            }

            if (parameters == null)
                parameters = new Dictionary<string, object>();

            // Set default values for unlinked parameters only
            foreach (ParameterField param in rd.ParameterFields)
            {
                // Skip subreport-linked parameters (ReportName is filled for subreports)
                if (string.IsNullOrEmpty(param.ReportName) && !parameters.ContainsKey(param.Name))
                {
                    if (param.Name.Equals("Letter head", StringComparison.OrdinalIgnoreCase))
                    {
                        parameters[param.Name] = "True";
                    }
                    else if (param.ParameterValueType == ParameterValueKind.StringParameter)
                        parameters[param.Name] = "";
                    else if (param.ParameterValueType == ParameterValueKind.NumberParameter)
                        parameters[param.Name] = 0;
                    else if (param.ParameterValueType == ParameterValueKind.DateParameter)
                        parameters[param.Name] = DateTime.Now;
                    else if (param.ParameterValueType == ParameterValueKind.BooleanParameter)
                        parameters[param.Name] = false;
                }
            }

            // Apply parameters
            foreach (var param in parameters)
            {
                rd.SetParameterValue(param.Key, param.Value);
            }

            if (!string.IsNullOrEmpty(recordSelectionFormula))
            {
                rd.RecordSelectionFormula = recordSelectionFormula;
            }

            // Ensure output directory exists
            string outputDirectory = Path.GetDirectoryName(outputFilePath);
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            // Export directly to file
            rd.ExportToDisk(ExportFormatType.PortableDocFormat, outputFilePath);
            rd.Close();
            rd.Dispose();
        }
        private static void GetConnectionInfo(string connectionStringName, out string server, out string database, out string userId, out string password, out bool integratedSecurity, out string providerName, out string fullConnectionString)
        {
            var settings = ConfigurationManager.ConnectionStrings[connectionStringName];
            fullConnectionString = settings.ConnectionString;
            providerName = settings.ProviderName;

            if (string.Equals(providerName, "System.Data.Odbc", StringComparison.OrdinalIgnoreCase))
            {
                var builder = new System.Data.Odbc.OdbcConnectionStringBuilder(fullConnectionString);
                
                // Extract Server
                if (builder.ContainsKey("Server"))
                    server = builder["Server"] as string;
                else if (builder.ContainsKey("Data Source"))
                    server = builder["Data Source"] as string;
                else
                    server = string.Empty;

                // Extract Database
                if (builder.ContainsKey("Database"))
                    database = builder["Database"] as string;
                else if (builder.ContainsKey("Initial Catalog"))
                    database = builder["Initial Catalog"] as string;
                else
                    database = string.Empty;

                // Extract Integrated Security
                integratedSecurity = false;
                if (builder.ContainsKey("Trusted_Connection"))
                {
                    string trusted = builder["Trusted_Connection"] as string;
                    integratedSecurity = "yes".Equals(trusted, StringComparison.OrdinalIgnoreCase) || "true".Equals(trusted, StringComparison.OrdinalIgnoreCase);
                }

                // Extract User/Password
                userId = string.Empty;
                if (builder.ContainsKey("User Id")) userId = builder["User Id"] as string;
                else if (builder.ContainsKey("Uid")) userId = builder["Uid"] as string;

                password = string.Empty;
                if (builder.ContainsKey("Password")) password = builder["Password"] as string;
                else if (builder.ContainsKey("Pwd")) password = builder["Pwd"] as string;
            }
            else
            {
                var builder = new SqlConnectionStringBuilder(fullConnectionString);
                server = builder.DataSource;
                database = builder.InitialCatalog;
                integratedSecurity = builder.IntegratedSecurity;
                userId = builder.UserID;
                password = builder.Password;
            }
        }
    }
}
