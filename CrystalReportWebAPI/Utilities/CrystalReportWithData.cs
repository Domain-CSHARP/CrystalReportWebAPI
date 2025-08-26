using CrystalDecisions.CrystalReports.Engine;
using CrystalDecisions.Shared;
using CrystalReportWebAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace CrystalReportWebAPI.Utilities
{
    public static class CrystalReportWithData
    {
        /// <summary>
        /// Renders a Crystal Report with recordset data
        /// </summary>
        /// <param name="request">Report request containing data and configuration</param>
        /// <returns>HTTP response with the generated report</returns>
        public static HttpResponseMessage RenderReportWithRecordset(RecordsetReportRequest request)
        {
            var rd = new ReportDocument();
            
            try
            {
                // Load the report
                string fullReportPath = Path.Combine(System.Web.Hosting.HostingEnvironment.MapPath(request.ReportPath), request.ReportFileName);
                rd.Load(fullReportPath);

                // Convert recordset data to DataTable
                DataTable dataTable = ConvertRecordsetToDataTable(request.RecordsetData);
                dataTable.TableName = request.DataSourceName;

                // Set the data source
                rd.SetDataSource(dataTable);

                // Set report parameters if any
                SetReportParameters(rd, request.Parameters);

                // Export the report
                return ExportReport(rd, request.ExportFilename, GetExportFormat(request.ExportFormat));
            }
            catch (Exception ex)
            {
                throw new Exception($"Error generating report: {ex.Message}", ex);
            }
            finally
            {
                rd?.Dispose();
            }
        }

        /// <summary>
        /// Renders a Crystal Report with DataTable
        /// </summary>
        /// <param name="request">Report request containing DataTable and configuration</param>
        /// <returns>HTTP response with the generated report</returns>
        public static HttpResponseMessage RenderReportWithDataTable(DataTableReportRequest request)
        {
            var rd = new ReportDocument();
            
            try
            {
                // Load the report
                string fullReportPath = Path.Combine(System.Web.Hosting.HostingEnvironment.MapPath(request.ReportPath), request.ReportFileName);
                rd.Load(fullReportPath);

                // Set the data source
                request.DataTable.TableName = request.DataSourceName;
                rd.SetDataSource(request.DataTable);

                // Set report parameters if any
                SetReportParameters(rd, request.Parameters);

                // Export the report
                return ExportReport(rd, request.ExportFilename, GetExportFormat(request.ExportFormat));
            }
            catch (Exception ex)
            {
                throw new Exception($"Error generating report: {ex.Message}", ex);
            }
            finally
            {
                rd?.Dispose();
            }
        }

        /// <summary>
        /// Renders a Crystal Report with multiple data sources
        /// </summary>
        /// <param name="reportPath">Path to the report file</param>
        /// <param name="reportFileName">Report file name</param>
        /// <param name="dataSources">Dictionary of data sources (key = table name, value = DataTable)</param>
        /// <param name="parameters">Report parameters</param>
        /// <param name="exportFilename">Export filename</param>
        /// <param name="exportFormat">Export format</param>
        /// <returns>HTTP response with the generated report</returns>
        public static HttpResponseMessage RenderReportWithMultipleDataSources(
            string reportPath, 
            string reportFileName, 
            Dictionary<string, DataTable> dataSources,
            Dictionary<string, object> parameters = null,
            string exportFilename = "report.pdf",
            string exportFormat = "PDF")
        {
            var rd = new ReportDocument();
            
            try
            {
                // Load the report
                string fullReportPath = Path.Combine(System.Web.Hosting.HostingEnvironment.MapPath(reportPath), reportFileName);
                rd.Load(fullReportPath);

                // Create a DataSet and add all data sources
                DataSet dataSet = new DataSet();
                foreach (var dataSource in dataSources)
                {
                    dataSource.Value.TableName = dataSource.Key;
                    dataSet.Tables.Add(dataSource.Value.Copy());
                }

                // Set the data source
                rd.SetDataSource(dataSet);

                // Set report parameters if any
                if (parameters != null)
                {
                    SetReportParameters(rd, parameters);
                }

                // Export the report
                return ExportReport(rd, exportFilename, GetExportFormat(exportFormat));
            }
            catch (Exception ex)
            {
                throw new Exception($"Error generating report: {ex.Message}", ex);
            }
            finally
            {
                rd?.Dispose();
            }
        }

        /// <summary>
        /// Converts a list of dictionaries to a DataTable
        /// </summary>
        /// <param name="recordsetData">List of dictionaries representing rows</param>
        /// <returns>DataTable with the converted data</returns>
        private static DataTable ConvertRecordsetToDataTable(List<Dictionary<string, object>> recordsetData)
        {
            DataTable dataTable = new DataTable();

            if (recordsetData == null || !recordsetData.Any())
            {
                return dataTable;
            }

            // Create columns based on the first row
            var firstRow = recordsetData.First();
            foreach (var kvp in firstRow)
            {
                Type columnType = kvp.Value?.GetType() ?? typeof(string);
                dataTable.Columns.Add(kvp.Key, columnType);
            }

            // Add rows
            foreach (var row in recordsetData)
            {
                DataRow dataRow = dataTable.NewRow();
                foreach (var kvp in row)
                {
                    dataRow[kvp.Key] = kvp.Value ?? DBNull.Value;
                }
                dataTable.Rows.Add(dataRow);
            }

            return dataTable;
        }

        /// <summary>
        /// Sets report parameters
        /// </summary>
        /// <param name="reportDocument">Report document</param>
        /// <param name="parameters">Parameters to set</param>
        private static void SetReportParameters(ReportDocument reportDocument, Dictionary<string, object> parameters)
        {
            if (parameters == null || !parameters.Any())
                return;

            foreach (var parameter in parameters)
            {
                try
                {
                    reportDocument.SetParameterValue(parameter.Key, parameter.Value);
                }
                catch (Exception ex)
                {
                    // Log parameter setting error but continue
                    System.Diagnostics.Debug.WriteLine($"Error setting parameter {parameter.Key}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Exports the report to the specified format
        /// </summary>
        /// <param name="reportDocument">Report document</param>
        /// <param name="exportFilename">Export filename</param>
        /// <param name="exportFormat">Export format</param>
        /// <returns>HTTP response with the exported report</returns>
        private static HttpResponseMessage ExportReport(ReportDocument reportDocument, string exportFilename, ExportFormatType exportFormat)
        {
            MemoryStream ms = new MemoryStream();
            
            using (var stream = reportDocument.ExportToStream(exportFormat))
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
                new System.Net.Http.Headers.MediaTypeHeaderValue(GetContentType(exportFormat));

            return result;
        }

        /// <summary>
        /// Gets the Crystal Reports export format from string
        /// </summary>
        /// <param name="format">Format string</param>
        /// <returns>ExportFormatType</returns>
        private static ExportFormatType GetExportFormat(string format)
        {
            switch (format?.ToUpper())
            {
                case "PDF":
                    return ExportFormatType.PortableDocFormat;
                case "EXCEL":
                case "XLS":
                    return ExportFormatType.Excel;
                case "XLSX":
                    return ExportFormatType.ExcelWorkbook;
                case "WORD":
                case "DOC":
                    return ExportFormatType.WordForWindows;
                case "RTF":
                    return ExportFormatType.RichText;
                case "CSV":
                    return ExportFormatType.CharacterSeparatedValues;
                case "XML":
                    return ExportFormatType.Xml;
                default:
                    return ExportFormatType.PortableDocFormat;
            }
        }

        /// <summary>
        /// Gets the MIME content type for the export format
        /// </summary>
        /// <param name="exportFormat">Export format</param>
        /// <returns>Content type string</returns>
        private static string GetContentType(ExportFormatType exportFormat)
        {
            switch (exportFormat)
            {
                case ExportFormatType.PortableDocFormat:
                    return "application/pdf";
                case ExportFormatType.Excel:
                case ExportFormatType.ExcelWorkbook:
                    return "application/vnd.ms-excel";
                case ExportFormatType.WordForWindows:
                    return "application/msword";
                case ExportFormatType.RichText:
                    return "application/rtf";
                case ExportFormatType.CharacterSeparatedValues:
                    return "text/csv";
                case ExportFormatType.Xml:
                    return "application/xml";
                default:
                    return "application/octet-stream";
            }
        }
    }
}
