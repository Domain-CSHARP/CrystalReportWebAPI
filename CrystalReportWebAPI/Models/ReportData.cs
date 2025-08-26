using System;
using System.Collections.Generic;
using System.Data;

namespace CrystalReportWebAPI.Models
{
    /// <summary>
    /// Base class for report data models
    /// </summary>
    public class ReportDataRequest
    {
        public string ReportPath { get; set; }
        public string ReportFileName { get; set; }
        public string ExportFilename { get; set; }
        public string ExportFormat { get; set; } = "PDF"; // PDF, Excel, Word, etc.
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Model for passing recordset data to Crystal Reports
    /// </summary>
    public class RecordsetReportRequest : ReportDataRequest
    {
        public List<Dictionary<string, object>> RecordsetData { get; set; } = new List<Dictionary<string, object>>();
        public string DataSourceName { get; set; } = "MainDataSource";
    }

    /// <summary>
    /// Model for DataTable-based reports
    /// </summary>
    public class DataTableReportRequest : ReportDataRequest
    {
        public DataTable DataTable { get; set; }
        public string DataSourceName { get; set; } = "MainDataSource";
    }

    /// <summary>
    /// Sample data models for common report scenarios
    /// </summary>
    public class CustomerReportData
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public DateTime CreatedDate { get; set; }
        public decimal TotalOrders { get; set; }
    }

    public class SalesReportData
    {
        public int OrderId { get; set; }
        public string CustomerName { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime OrderDate { get; set; }
        public string SalesRep { get; set; }
    }

    public class InventoryReportData
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public string Category { get; set; }
        public int StockQuantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalValue { get; set; }
        public string Supplier { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
