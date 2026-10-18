using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using InventoryManagementSystem.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers
{
    [Authorize]
    public class ReportController : Controller
    {
        private readonly ISaleRepository _saleRepository;
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IReportService _reportService;
        private readonly IUserRepository _userRepository;

        public ReportController(
            ISaleRepository saleRepository,
            IProductService productService,
            ICategoryService categoryService,
            IReportService reportService,
            IUserRepository userRepository)
        {
            _saleRepository = saleRepository;
            _productService = productService;
            _categoryService = categoryService;
            _reportService = reportService;
            _userRepository = userRepository;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string reportType = "Sales",
            string datePreset = "ThisMonth",
            string? startDate = null,
            string? endDate = null,
            string? categoryId = null,
            string? productId = null)
        {
            var categories = await _categoryService.GetAllCategoriesAsync();
            var products = await _productService.GetAllProductsAsync();
            var users = await _userRepository.GetAllAsync();

            ViewBag.Categories = categories;
            ViewBag.Products = products;
            ViewBag.Users = users;

            ViewBag.InitialReportType = reportType;
            ViewBag.InitialDatePreset = datePreset;
            ViewBag.InitialStartDate = startDate;
            ViewBag.InitialEndDate = endDate;
            ViewBag.InitialCategoryId = categoryId;
            ViewBag.InitialProductId = productId;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Preview([FromQuery] ReportFilterRequest request)
        {
            try
            {
                var reportData = await _reportService.BuildReportDataAsync(request);
                return Json(new { success = true, data = reportData });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to generate report preview: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportExcel([FromQuery] ReportFilterRequest request)
        {
            try
            {
                // Force page size to max for full export
                request.Page = 1;
                request.PageSize = 100000;

                var reportData = await _reportService.BuildReportDataAsync(request);
                var fileBytes = _reportService.GenerateExcelReport(reportData);

                string fileName = $"SIMS_{request.ReportType}_Report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest("Error generating Excel report: " + ex.Message);
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportPdf([FromQuery] ReportFilterRequest request)
        {
            try
            {
                // Force page size for PDF export
                request.Page = 1;
                request.PageSize = 5000;

                var reportData = await _reportService.BuildReportDataAsync(request);
                var fileBytes = _reportService.GeneratePdfReport(reportData);

                string fileName = $"SIMS_{request.ReportType}_Report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
                return File(fileBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest("Error generating PDF report: " + ex.Message);
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportCsv([FromQuery] ReportFilterRequest request)
        {
            try
            {
                request.Page = 1;
                request.PageSize = 100000;

                var reportData = await _reportService.BuildReportDataAsync(request);
                var fileBytes = _reportService.GenerateCsvReport(reportData);

                string fileName = $"SIMS_{request.ReportType}_Report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
                return File(fileBytes, "text/csv; charset=utf-8", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest("Error generating CSV report: " + ex.Message);
            }
        }

        #region Legacy Compatibility Endpoints

        [HttpGet]
        public async Task<IActionResult> ExportSalesExcel(string startDate, string endDate)
        {
            DateTime.TryParse(startDate, out var start);
            DateTime.TryParse(endDate, out var end);
            var req = new ReportFilterRequest { ReportType = "Sales", DatePreset = "Custom", StartDate = start, EndDate = end };
            return await ExportExcel(req);
        }

        [HttpGet]
        public async Task<IActionResult> ExportInventoryExcel()
        {
            var req = new ReportFilterRequest { ReportType = "Inventory", DatePreset = "AllTime" };
            return await ExportExcel(req);
        }

        [HttpGet]
        public async Task<IActionResult> ExportInventoryPdf()
        {
            var req = new ReportFilterRequest { ReportType = "Inventory", DatePreset = "AllTime" };
            return await ExportPdf(req);
        }

        #endregion
    }
}
