# Crystal Reports with Recordset Data - Implementation Guide

This document explains how to use the enhanced Crystal Reports Web API that supports passing recordset values to Crystal Reports.

## Overview

The enhanced implementation provides several ways to generate Crystal Reports with dynamic data:

1. **Recordset Data Binding** - Pass data as a list of dictionaries
2. **DataTable Binding** - Pass data as a .NET DataTable
3. **Multiple Data Sources** - Support for reports with multiple data sources
4. **Parameter Support** - Pass parameters to Crystal Reports
5. **Multiple Export Formats** - PDF, Excel, Word, CSV, XML, etc.

## New API Endpoints

### 1. Generate Report with Recordset Data
**POST** `/api/Reports/GenerateWithRecordset`

Accepts a JSON payload with recordset data and generates a Crystal Report.

#### Request Body Example:
```json
{
  "ReportPath": "~/Reports/Custom",
  "ReportFileName": "CustomerReport.rpt",
  "ExportFilename": "CustomerReport.pdf",
  "ExportFormat": "PDF",
  "DataSourceName": "CustomerData",
  "RecordsetData": [
    {
      "CustomerId": 1,
      "CustomerName": "John Doe",
      "Email": "john.doe@email.com",
      "Phone": "+1-555-0123",
      "Address": "123 Main St, Anytown, USA",
      "CreatedDate": "2024-07-27T00:00:00",
      "TotalOrders": 15.50
    },
    {
      "CustomerId": 2,
      "CustomerName": "Jane Smith",
      "Email": "jane.smith@email.com",
      "Phone": "+1-555-0456",
      "Address": "456 Oak Ave, Another City, USA",
      "CreatedDate": "2024-07-12T00:00:00",
      "TotalOrders": 32.75
    }
  ],
  "Parameters": {
    "ReportTitle": "Customer Report",
    "GeneratedDate": "2024-08-26T00:00:00"
  }
}
```

### 2. Generate Report with DataTable
**POST** `/api/Reports/GenerateWithDataTable`

Similar to recordset but accepts a DataTable structure.

### 3. Sample Reports (GET Endpoints)
- **GET** `/api/Reports/Sample/CustomerReport` - Sample customer report
- **GET** `/api/Reports/Sample/SalesReport` - Sample sales report
- **GET** `/api/Reports/Sample/ExistingDataReport` - Report using existing data

## Data Models

### RecordsetReportRequest
```csharp
public class RecordsetReportRequest : ReportDataRequest
{
    public List<Dictionary<string, object>> RecordsetData { get; set; }
    public string DataSourceName { get; set; } = "MainDataSource";
}
```

### ReportDataRequest (Base Class)
```csharp
public class ReportDataRequest
{
    public string ReportPath { get; set; }
    public string ReportFileName { get; set; }
    public string ExportFilename { get; set; }
    public string ExportFormat { get; set; } = "PDF";
    public Dictionary<string, object> Parameters { get; set; }
}
```

## Supported Export Formats

- **PDF** - Portable Document Format (default)
- **EXCEL/XLS** - Microsoft Excel
- **XLSX** - Excel Workbook
- **WORD/DOC** - Microsoft Word
- **RTF** - Rich Text Format
- **CSV** - Comma Separated Values
- **XML** - XML format

## Usage Examples

### Example 1: C# Client Code
```csharp
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

public async Task<byte[]> GenerateCustomerReport()
{
    var client = new HttpClient();
    var baseUrl = "https://your-api-url.com";
    
    var request = new
    {
        ReportPath = "~/Reports/Custom",
        ReportFileName = "CustomerReport.rpt",
        ExportFilename = "CustomerReport.pdf",
        ExportFormat = "PDF",
        DataSourceName = "CustomerData",
        RecordsetData = new[]
        {
            new
            {
                CustomerId = 1,
                CustomerName = "John Doe",
                Email = "john.doe@email.com",
                Phone = "+1-555-0123",
                Address = "123 Main St, Anytown, USA",
                CreatedDate = DateTime.Now.AddDays(-30),
                TotalOrders = 15.50m
            }
        },
        Parameters = new
        {
            ReportTitle = "Customer Report",
            GeneratedDate = DateTime.Now
        }
    };
    
    var json = JsonConvert.SerializeObject(request);
    var content = new StringContent(json, Encoding.UTF8, "application/json");
    
    var response = await client.PostAsync($"{baseUrl}/api/Reports/GenerateWithRecordset", content);
    
    if (response.IsSuccessStatusCode)
    {
        return await response.Content.ReadAsByteArrayAsync();
    }
    
    throw new Exception($"Error generating report: {response.StatusCode}");
}
```

### Example 2: JavaScript/jQuery AJAX
```javascript
function generateReport() {
    var requestData = {
        ReportPath: "~/Reports/Custom",
        ReportFileName: "SalesReport.rpt",
        ExportFilename: "SalesReport.pdf",
        ExportFormat: "PDF",
        DataSourceName: "SalesData",
        RecordsetData: [
            {
                OrderId: 1001,
                CustomerName: "John Doe",
                ProductName: "Widget A",
                Quantity: 5,
                UnitPrice: 19.99,
                TotalAmount: 99.95,
                OrderDate: new Date(),
                SalesRep: "Alice Johnson"
            }
        ],
        Parameters: {
            ReportTitle: "Sales Report",
            GeneratedDate: new Date()
        }
    };
    
    $.ajax({
        url: '/api/Reports/GenerateWithRecordset',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(requestData),
        xhrFields: {
            responseType: 'blob'
        },
        success: function(data) {
            // Create download link
            var blob = new Blob([data], { type: 'application/pdf' });
            var url = window.URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url;
            a.download = 'SalesReport.pdf';
            document.body.appendChild(a);
            a.click();
            window.URL.revokeObjectURL(url);
            document.body.removeChild(a);
        },
        error: function(xhr, status, error) {
            console.error('Error generating report:', error);
        }
    });
}
```

### Example 3: PowerShell
```powershell
$baseUrl = "https://your-api-url.com"
$endpoint = "$baseUrl/api/Reports/GenerateWithRecordset"

$requestBody = @{
    ReportPath = "~/Reports/Custom"
    ReportFileName = "CustomerReport.rpt"
    ExportFilename = "CustomerReport.pdf"
    ExportFormat = "PDF"
    DataSourceName = "CustomerData"
    RecordsetData = @(
        @{
            CustomerId = 1
            CustomerName = "John Doe"
            Email = "john.doe@email.com"
            Phone = "+1-555-0123"
            Address = "123 Main St, Anytown, USA"
            CreatedDate = (Get-Date).AddDays(-30)
            TotalOrders = 15.50
        }
    )
    Parameters = @{
        ReportTitle = "Customer Report"
        GeneratedDate = Get-Date
    }
} | ConvertTo-Json -Depth 3

$response = Invoke-RestMethod -Uri $endpoint -Method Post -Body $requestBody -ContentType "application/json"
```

## Crystal Reports Setup

### 1. Creating a Crystal Report for Recordset Data

1. **Create a new Crystal Report** in Crystal Reports Designer
2. **Choose Data Source**: Select "Create New Connection" > "ADO.NET (XML)"
3. **Set up the data source** to match your recordset structure
4. **Design your report** with the fields from your data source
5. **Add parameters** if needed (optional)
6. **Save the report** to the appropriate folder in your project

### 2. Data Source Naming

When creating your Crystal Report, ensure the data source name matches the `DataSourceName` property in your request. The default is "MainDataSource".

### 3. Field Mapping

The field names in your Crystal Report must match the keys in your recordset data dictionaries. For example:
- If your recordset has `{"CustomerName": "John Doe"}`, your Crystal Report should have a field named `CustomerName`

## Error Handling

The API returns appropriate HTTP status codes:

- **200 OK** - Report generated successfully
- **400 Bad Request** - Invalid request data
- **500 Internal Server Error** - Server error during report generation

Error responses include detailed error messages in the response body.

## Performance Considerations

1. **Data Size**: Large recordsets may impact performance. Consider pagination for very large datasets.
2. **Caching**: The existing caching mechanism (`ClientCacheWithEtag`) is available for static reports.
3. **Memory Usage**: Reports are generated in memory. Monitor memory usage for high-volume scenarios.
4. **Concurrent Requests**: Crystal Reports has licensing limitations on concurrent usage.

## Security Considerations

1. **Input Validation**: Always validate input data before processing
2. **File Paths**: Ensure report paths are within allowed directories
3. **Authentication**: Consider adding authentication to report endpoints
4. **Data Sanitization**: Sanitize data to prevent injection attacks

## Troubleshooting

### Common Issues:

1. **Report Not Found**: Verify the `ReportPath` and `ReportFileName` are correct
2. **Field Mapping Errors**: Ensure Crystal Report field names match recordset keys
3. **Data Type Mismatches**: Verify data types in recordset match Crystal Report expectations
4. **Parameter Errors**: Check parameter names and types match Crystal Report parameters

### Debug Tips:

1. Test with sample endpoints first (`/api/Reports/Sample/CustomerReport`)
2. Check server logs for detailed error messages
3. Verify Crystal Reports runtime is properly installed
4. Test with small datasets first

## Advanced Usage

### Multiple Data Sources
```csharp
var dataSources = new Dictionary<string, DataTable>
{
    {"Customers", customerDataTable},
    {"Orders", orderDataTable},
    {"Products", productDataTable}
};

var result = CrystalReportWithData.RenderReportWithMultipleDataSources(
    "~/Reports/Complex",
    "MasterDetailReport.rpt",
    dataSources,
    parameters,
    "ComplexReport.pdf",
    "PDF"
);
```

### Custom Data Loading
You can extend the system to load data from various sources:
- Database queries
- Web services
- XML files
- CSV files
- External APIs

## Conclusion

This enhanced Crystal Reports Web API provides flexible options for generating reports with dynamic data. The recordset approach allows for easy integration with various data sources while maintaining the power and formatting capabilities of Crystal Reports.

For additional support or questions, refer to the Crystal Reports documentation or contact your development team.
