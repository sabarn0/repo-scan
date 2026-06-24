using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DriveScanner.Api.Models;
using DriveScanner.Api.Services;

namespace DriveScanner.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScanController : ControllerBase
    {
        private readonly ScannerService _scannerService;
        private readonly ScanResultStore _resultStore;
        private readonly ExcelExportService _excelExportService;

        public ScanController(
            ScannerService scannerService,
            ScanResultStore resultStore,
            ExcelExportService excelExportService)
        {
            _scannerService = scannerService;
            _resultStore = resultStore;
            _excelExportService = excelExportService;
        }

        [HttpPost]
        public IActionResult StartScan([FromBody] ScanRequest request, [FromQuery] string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return BadRequest("SessionId is required.");
            }

            if (request == null)
            {
                return BadRequest("Scan configuration is required.");
            }

            _scannerService.StartScanAsync(request, sessionId);
            return Ok(new { Message = "Scan started successfully" });
        }

        [HttpPost("cancel")]
        public IActionResult CancelScan()
        {
            _scannerService.CancelScan();
            return Ok(new { Message = "Scan cancellation requested" });
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            return Ok(_resultStore.Progress);
        }

        [HttpGet("results")]
        public IActionResult GetResults([FromQuery] int offset = 0, [FromQuery] int limit = 50, [FromQuery] string? category = null)
        {
            var query = _resultStore.Matches.AsEnumerable();

            if (!string.IsNullOrEmpty(category))
            {
                query = query.Where(m => m.CategoryName.Equals(category, StringComparison.OrdinalIgnoreCase));
            }

            var totalCount = query.Count();
            var matches = query.Skip(offset).Take(limit).ToList();

            return Ok(new
            {
                TotalCount = totalCount,
                Sample = matches
            });
        }

        [HttpGet("export")]
        public IActionResult ExportResults()
        {
            try
            {
                var excelBytes = _excelExportService.GenerateExcelReport();
                return File(
                    excelBytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"ScanReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                );
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error exporting report: {ex.Message}");
            }
        }
    }
}
