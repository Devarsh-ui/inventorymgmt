using InventoryManagementSystem.Models;
using InventoryManagementSystem.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Interfaces
{
    public interface IReportService
    {
        Task<ReportResultData> BuildReportDataAsync(ReportFilterRequest request);
        byte[] GenerateExcelReport(ReportResultData data);
        byte[] GeneratePdfReport(ReportResultData data);
        byte[] GenerateCsvReport(ReportResultData data);

        // Legacy compatibility overloads
        byte[] GenerateSalesExcelReport(DateTime start, DateTime end, IEnumerable<Sale> sales);
        byte[] GenerateInventoryValuationExcelReport(IEnumerable<Product> products, Dictionary<string, string> categoryNames);
        byte[] GenerateInventoryPdfReport(IEnumerable<Product> products, Dictionary<string, string> categoryNames);
    }
}
