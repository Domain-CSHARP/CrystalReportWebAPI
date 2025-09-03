using CrystalDecisions.CrystalReports.Engine;
using CrystalDecisions.Shared;
using System;
using System.Collections.Generic;
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
    }
}
