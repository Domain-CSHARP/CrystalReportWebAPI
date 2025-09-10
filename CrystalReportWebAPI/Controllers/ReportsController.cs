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
        [Route("Mewah/TaxInvoice_SalesLocal_EINV/{referenceNumber}")]
        [HttpGet]
        [ClientCacheWithEtag(60)]  //1 min client side caching
        public HttpResponseMessage MewahTaxInvoiceSalesLocalEINV(string referenceNumber)
        {
            string reportPath = "~/Reports/Mewah";
            string reportFileName = "TaxInvoice_SalesLocal_EINV.rpt";
            string exportFilename = "TaxInvoice_SalesLocal_EINV.pdf";

            string recordSelectionFormula = "{INVOICE.INVOICE} = '" + referenceNumber + "'";
            HttpResponseMessage result = CrystalReport.RenderReport(reportPath, reportFileName, exportFilename, null, recordSelectionFormula);
            //HttpResponseMessage result = CrystalReport.RenderReport(reportPath, reportFileName, exportFilename);
            return result;
        }
    }
}
