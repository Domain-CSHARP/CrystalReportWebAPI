using CrystalReportWebAPI.Models;
using CrystalReportWebAPI.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Net.Http;
using System.Web.Http;

namespace CrystalReportWebAPI.Controllers
{
    [RoutePrefix("api/Reports")]
    public class ReportsController : ApiController
    {
        [AllowAnonymous]
        [Route("Financial/VarianceAnalysisReport")]
        [HttpGet]
        [ClientCacheWithEtag(60)]  //1 min client side caching
        public HttpResponseMessage FinancialVarianceAnalysisReport()
        {
            string reportPath = "~/Reports/Financial";
            string reportFileName = "YTDVarianceCrossTab.rpt";
            string exportFilename = "YTDVarianceCrossTab.pdf";

            HttpResponseMessage result = CrystalReport.RenderReport(reportPath, reportFileName, exportFilename);
            return result;
        }

        [AllowAnonymous]
        [Route("Demonstration/ComparativeIncomeStatement")]
        [HttpGet]
        [ClientCacheWithEtag(60)]  //1 min client side caching
        public HttpResponseMessage DemonstrationComparativeIncomeStatement()
        {
            string reportPath = "~/Reports/Demonstration";
            string reportFileName = "ComparativeIncomeStatement.rpt";
            string exportFilename = "ComparativeIncomeStatement.pdf";

            HttpResponseMessage result = CrystalReport.RenderReport(reportPath, reportFileName, exportFilename);
            return result;
        }

        [AllowAnonymous]
        [Route("VersatileandPrecise/Invoice")]
        [HttpGet]
        [ClientCacheWithEtag(60)]  //1 min client side caching
        public HttpResponseMessage VersatileandPreciseInvoice()
        {
            string reportPath = "~/Reports/VersatileandPrecise";
            string reportFileName = "Invoice.rpt";
            string exportFilename = "Invoice.pdf";

            HttpResponseMessage result = CrystalReport.RenderReport(reportPath, reportFileName, exportFilename);
            return result;
        }

        [AllowAnonymous]
        [Route("VersatileandPrecise/FortifyFinancialAllinOneRetirementSavings")]
        [HttpGet]
        [ClientCacheWithEtag(60)]  //1 min client side caching
        public HttpResponseMessage VersatileandPreciseFortifyFinancialAllinOneRetirementSavings()
        {
            string reportPath = "~/Reports/VersatileandPrecise";
            string reportFileName = "FortifyFinancialAllinOneRetirementSavings.rpt";
            string exportFilename = "FortifyFinancialAllinOneRetirementSavings.pdf";

            HttpResponseMessage result = CrystalReport.RenderReport(reportPath, reportFileName, exportFilename);

            return result;
        }

        // ========== NEW METHODS FOR RECORDSET DATA BINDING ==========

        /// <summary>
        /// Generates a report with recordset data passed via POST request
        /// </summary>
        /// <param name="request">Report request containing recordset data</param>
        /// <returns>Generated report as HTTP response</returns>
        [AllowAnonymous]
        [Route("GenerateWithRecordset")]
        [HttpPost]
        public HttpResponseMessage GenerateReportWithRecordset([FromBody] RecordsetReportRequest request)
        {
            try
            {
                if (request == null)
                {
                    return Request.CreateErrorResponse(System.Net.HttpStatusCode.BadRequest, "Request cannot be null");
                }

                if (string.IsNullOrEmpty(request.ReportPath) || string.IsNullOrEmpty(request.ReportFileName))
                {
                    return Request.CreateErrorResponse(System.Net.HttpStatusCode.BadRequest, "ReportPath and ReportFileName are required");
                }

                HttpResponseMessage result = CrystalReportWithData.RenderReportWithRecordset(request);
                return result;
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(System.Net.HttpStatusCode.InternalServerError, $"Error generating report: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates a report with DataTable passed via POST request
        /// </summary>
        /// <param name="request">Report request containing DataTable</param>
        /// <returns>Generated report as HTTP response</returns>
        [AllowAnonymous]
        [Route("GenerateWithDataTable")]
        [HttpPost]
        public HttpResponseMessage GenerateReportWithDataTable([FromBody] DataTableReportRequest request)
        {
            try
            {
                if (request == null)
                {
                    return Request.CreateErrorResponse(System.Net.HttpStatusCode.BadRequest, "Request cannot be null");
                }

                if (string.IsNullOrEmpty(request.ReportPath) || string.IsNullOrEmpty(request.ReportFileName))
                {
                    return Request.CreateErrorResponse(System.Net.HttpStatusCode.BadRequest, "ReportPath and ReportFileName are required");
                }

                if (request.DataTable == null)
                {
                    return Request.CreateErrorResponse(System.Net.HttpStatusCode.BadRequest, "DataTable cannot be null");
                }

                HttpResponseMessage result = CrystalReportWithData.RenderReportWithDataTable(request);
                return result;
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(System.Net.HttpStatusCode.InternalServerError, $"Error generating report: {ex.Message}");
            }
        }

        /// <summary>
        /// Sample endpoint demonstrating customer report generation with sample data
        /// </summary>
        /// <returns>Customer report with sample data</returns>
        [AllowAnonymous]
        [Route("Sample/CustomerReport")]
        [HttpGet]
        public HttpResponseMessage GenerateSampleCustomerReport()
        {
            try
            {
                // Create sample customer data
                var customerData = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>
                    {
                        {"CustomerId", 1},
                        {"CustomerName", "John Doe"},
                        {"Email", "john.doe@email.com"},
                        {"Phone", "+1-555-0123"},
                        {"Address", "123 Main St, Anytown, USA"},
                        {"CreatedDate", DateTime.Now.AddDays(-30)},
                        {"TotalOrders", 15.50m}
                    },
                    new Dictionary<string, object>
                    {
                        {"CustomerId", 2},
                        {"CustomerName", "Jane Smith"},
                        {"Email", "jane.smith@email.com"},
                        {"Phone", "+1-555-0456"},
                        {"Address", "456 Oak Ave, Another City, USA"},
                        {"CreatedDate", DateTime.Now.AddDays(-45)},
                        {"TotalOrders", 32.75m}
                    },
                    new Dictionary<string, object>
                    {
                        {"CustomerId", 3},
                        {"CustomerName", "Bob Johnson"},
                        {"Email", "bob.johnson@email.com"},
                        {"Phone", "+1-555-0789"},
                        {"Address", "789 Pine Rd, Third Town, USA"},
                        {"CreatedDate", DateTime.Now.AddDays(-60)},
                        {"TotalOrders", 8.25m}
                    }
                };

                var request = new RecordsetReportRequest
                {
                    ReportPath = "~/Reports/Custom", // You would need to create this path and report
                    ReportFileName = "CustomerReport.rpt", // You would need to create this report
                    ExportFilename = "CustomerReport.pdf",
                    ExportFormat = "PDF",
                    RecordsetData = customerData,
                    DataSourceName = "CustomerData",
                    Parameters = new Dictionary<string, object>
                    {
                        {"ReportTitle", "Customer Report"},
                        {"GeneratedDate", DateTime.Now}
                    }
                };

                HttpResponseMessage result = CrystalReportWithData.RenderReportWithRecordset(request);
                return result;
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(System.Net.HttpStatusCode.InternalServerError, $"Error generating sample report: {ex.Message}");
            }
        }

        /// <summary>
        /// Sample endpoint demonstrating sales report generation with sample data
        /// </summary>
        /// <returns>Sales report with sample data</returns>
        [AllowAnonymous]
        [Route("Sample/SalesReport")]
        [HttpGet]
        public HttpResponseMessage GenerateSampleSalesReport()
        {
            try
            {
                // Create sample sales data
                var salesData = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>
                    {
                        {"OrderId", 1001},
                        {"CustomerName", "John Doe"},
                        {"ProductName", "Widget A"},
                        {"Quantity", 5},
                        {"UnitPrice", 19.99m},
                        {"TotalAmount", 99.95m},
                        {"OrderDate", DateTime.Now.AddDays(-10)},
                        {"SalesRep", "Alice Johnson"}
                    },
                    new Dictionary<string, object>
                    {
                        {"OrderId", 1002},
                        {"CustomerName", "Jane Smith"},
                        {"ProductName", "Widget B"},
                        {"Quantity", 3},
                        {"UnitPrice", 29.99m},
                        {"TotalAmount", 89.97m},
                        {"OrderDate", DateTime.Now.AddDays(-8)},
                        {"SalesRep", "Bob Wilson"}
                    },
                    new Dictionary<string, object>
                    {
                        {"OrderId", 1003},
                        {"CustomerName", "Bob Johnson"},
                        {"ProductName", "Widget C"},
                        {"Quantity", 2},
                        {"UnitPrice", 39.99m},
                        {"TotalAmount", 79.98m},
                        {"OrderDate", DateTime.Now.AddDays(-5)},
                        {"SalesRep", "Alice Johnson"}
                    }
                };

                var request = new RecordsetReportRequest
                {
                    ReportPath = "~/Reports/Custom", // You would need to create this path and report
                    ReportFileName = "SalesReport.rpt", // You would need to create this report
                    ExportFilename = "SalesReport.pdf",
                    ExportFormat = "PDF",
                    RecordsetData = salesData,
                    DataSourceName = "SalesData",
                    Parameters = new Dictionary<string, object>
                    {
                        {"ReportTitle", "Sales Report"},
                        {"GeneratedDate", DateTime.Now},
                        {"DateRange", $"{DateTime.Now.AddDays(-30):yyyy-MM-dd} to {DateTime.Now:yyyy-MM-dd}"}
                    }
                };

                HttpResponseMessage result = CrystalReportWithData.RenderReportWithRecordset(request);
                return result;
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(System.Net.HttpStatusCode.InternalServerError, $"Error generating sample sales report: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates a report using existing XML data files (demonstrates using existing data sources)
        /// </summary>
        /// <returns>Report using existing XML data</returns>
        [AllowAnonymous]
        [Route("Sample/ExistingDataReport")]
        [HttpGet]
        public HttpResponseMessage GenerateReportWithExistingData()
        {
            try
            {
                // This demonstrates how to use existing data files
                // You can load data from XML, database, or any other source
                var request = new RecordsetReportRequest
                {
                    ReportPath = "~/Reports/Demonstration",
                    ReportFileName = "WorldSalesReport.rpt", // Using existing report
                    ExportFilename = "WorldSalesReport_WithData.pdf",
                    ExportFormat = "PDF",
                    RecordsetData = GetSampleWorldSalesData(), // Custom data
                    DataSourceName = "WorldSalesData"
                };

                HttpResponseMessage result = CrystalReportWithData.RenderReportWithRecordset(request);
                return result;
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(System.Net.HttpStatusCode.InternalServerError, $"Error generating report with existing data: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to generate sample world sales data
        /// </summary>
        /// <returns>List of sample world sales data</returns>
        private List<Dictionary<string, object>> GetSampleWorldSalesData()
        {
            return new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    {"Country", "United States"},
                    {"Region", "North America"},
                    {"Sales", 1250000.00m},
                    {"Year", 2024},
                    {"Quarter", "Q1"}
                },
                new Dictionary<string, object>
                {
                    {"Country", "Canada"},
                    {"Region", "North America"},
                    {"Sales", 850000.00m},
                    {"Year", 2024},
                    {"Quarter", "Q1"}
                },
                new Dictionary<string, object>
                {
                    {"Country", "United Kingdom"},
                    {"Region", "Europe"},
                    {"Sales", 950000.00m},
                    {"Year", 2024},
                    {"Quarter", "Q1"}
                },
                new Dictionary<string, object>
                {
                    {"Country", "Germany"},
                    {"Region", "Europe"},
                    {"Sales", 1100000.00m},
                    {"Year", 2024},
                    {"Quarter", "Q1"}
                },
                new Dictionary<string, object>
                {
                    {"Country", "Japan"},
                    {"Region", "Asia"},
                    {"Sales", 750000.00m},
                    {"Year", 2024},
                    {"Quarter", "Q1"}
                }
            };
        }
    }
}
