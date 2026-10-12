using InventoryManagementSystem.DTOs;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Services
{
    public class MobileSpecSearchService : IMobileSpecSearchService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _memoryCache;
        private readonly IManufacturerRegistryService _registryService;
        private static readonly PropertyInfo[] SpecProperties = typeof(MobileSpecifications).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        public MobileSpecSearchService(
            HttpClient httpClient,
            IMemoryCache memoryCache,
            IManufacturerRegistryService registryService)
        {
            _httpClient = httpClient;
            _memoryCache = memoryCache;
            _registryService = registryService;
        }

        public async Task<MobileSpecSearchResultDto> SearchSpecificationsAsync(
            string brand,
            string modelName,
            string variant,
            bool allowThirdPartyFallback = false,
            string? customUrl = null,
            bool forceRefresh = false)
        {
            brand = (brand ?? string.Empty).Trim();
            modelName = (modelName ?? string.Empty).Trim();
            variant = (variant ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(brand) && string.IsNullOrWhiteSpace(modelName) && string.IsNullOrWhiteSpace(customUrl))
            {
                return new MobileSpecSearchResultDto
                {
                    Success = false,
                    ErrorMessage = "Please enter at least a Brand, Model Name, or Direct Specification URL."
                };
            }

            string cacheKey = $"FullSpecParity_{brand.ToLowerInvariant()}_{modelName.ToLowerInvariant()}_{variant.ToLowerInvariant()}_{allowThirdPartyFallback}_{customUrl?.ToLowerInvariant()}";

            if (!forceRefresh && _memoryCache.TryGetValue(cacheKey, out MobileSpecSearchResultDto? cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            var result = new MobileSpecSearchResultDto
            {
                Brand = brand,
                ModelName = modelName,
                Variant = variant,
                PrimarySourceType = "Official Manufacturer",
                TotalFieldsCount = SpecProperties.Length
            };

            var manufacturer = _registryService.GetSourceForBrand(brand);
            if (manufacturer != null)
            {
                result.OfficialDomain = manufacturer.RegionalDomains.FirstOrDefault() ?? manufacturer.OfficialDomain;
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));

                // Path A: Custom URL entered directly by Admin
                if (!string.IsNullOrWhiteSpace(customUrl))
                {
                    if (!IsUrlSafe(customUrl))
                    {
                        result.Success = false;
                        result.ErrorMessage = "Security constraint: Provided URL failed SSRF safety checks.";
                        return result;
                    }

                    bool isOfficialUrl = manufacturer != null && !string.IsNullOrWhiteSpace(result.OfficialDomain) && customUrl.Contains(result.OfficialDomain, StringComparison.OrdinalIgnoreCase);
                    bool customSuccess = await FetchAndExtractFromUrlAsync(customUrl, result, isOfficial: isOfficialUrl, cts.Token);
                    if (customSuccess)
                    {
                        result.Success = true;
                        result.PrimarySourceType = isOfficialUrl ? "Official Manufacturer" : "Direct Reference Page";
                        result.ConfidenceMatch = "High";
                        result.ConfidenceReason = "Specifications extracted directly from provided URL.";
                        ComputeStatistics(result, isOfficialUrl ? "Official" : "Third-Party");
                        _memoryCache.Set(cacheKey, result, TimeSpan.FromHours(24));
                        return result;
                    }
                    else
                    {
                        result.Success = false;
                        result.ErrorMessage = "Unable to extract specifications from the provided URL. Please verify the link.";
                        return result;
                    }
                }

                // Path B: Official Manufacturer Search First
                if (manufacturer != null && !string.IsNullOrWhiteSpace(result.OfficialDomain))
                {
                    string targetDomain = result.OfficialDomain;
                    string searchPattern = $"site:{targetDomain} \"{brand} {modelName}\"";
                    string searchUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(searchPattern)}";

                    if (IsUrlSafe(searchUrl))
                    {
                        var discoveredOfficialUrls = await DiscoverUrlsAsync(searchUrl, targetDomain, cts.Token);

                        foreach (var urlItem in discoveredOfficialUrls)
                        {
                            bool fetched = await FetchAndExtractFromUrlAsync(urlItem.Url, result, isOfficial: true, cts.Token);
                            if (fetched && result.ExactModelMatched && result.PopulatedFieldsCount > 3)
                            {
                                result.Success = true;
                                result.PrimarySourceType = "Official Manufacturer";
                                result.ConfidenceMatch = "High";
                                result.ConfidenceReason = $"Extracted official specifications from {urlItem.SiteName}.";
                                ComputeStatistics(result, "Official");
                                _memoryCache.Set(cacheKey, result, TimeSpan.FromHours(24));
                                return result;
                            }
                        }
                    }
                }

                // Path C: Third-Party Search (GSMArena, 91mobiles, DeviceSpecifications fallback)
                // If official site failed or third party fallback is enabled
                string queryStr = $"{brand} {modelName} {variant} mobile specs specifications".Trim();
                string fallbackSearchUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(queryStr)}";

                if (IsUrlSafe(fallbackSearchUrl))
                {
                    var thirdPartyUrls = await DiscoverUrlsAsync(fallbackSearchUrl, filterDomain: null, cts.Token);

                    // Prioritize GSMArena, 91mobiles, DeviceSpecifications
                    var sortedUrls = thirdPartyUrls
                        .OrderByDescending(x => x.Url.Contains("gsmarena.com") ? 3 : (x.Url.Contains("91mobiles.com") ? 2 : (x.Url.Contains("devicespecifications.com") ? 1 : 0)))
                        .ToList();

                    foreach (var tpUrl in sortedUrls.Take(3))
                    {
                        bool fetched = await FetchAndExtractFromUrlAsync(tpUrl.Url, result, isOfficial: false, cts.Token);
                        if (fetched && result.PopulatedFieldsCount > 3)
                        {
                            result.Success = true;
                            result.PrimarySourceType = "Third-Party Reference";
                            result.ConfidenceMatch = "Needs Verification";
                            result.ConfidenceReason = $"Extracted from reference site: {tpUrl.SiteName}. Please review before applying.";
                            ComputeStatistics(result, "Third-Party");
                            _memoryCache.Set(cacheKey, result, TimeSpan.FromHours(24));
                            return result;
                        }
                    }
                }

                // Path D: Specs Not Found
                result.Success = false;
                result.PrimarySourceType = "Official Manufacturer";
                result.ErrorMessage = $"Could not automatically extract specifications for '{brand} {modelName}'. You can paste the direct specification page URL above.";
                ComputeStatistics(result, "Unavailable");
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Specification search error: {ex.Message}";
                ComputeStatistics(result, "Unavailable");
                return result;
            }
        }

        private async Task<List<SpecSourceItem>> DiscoverUrlsAsync(string searchUrl, string? filterDomain, CancellationToken ct)
        {
            var list = new List<SpecSourceItem>();
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                var resp = await _httpClient.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) return list;

                string html = await resp.Content.ReadAsStringAsync();
                var matches = Regex.Matches(html, @"<a[^>]+href=[""'](?<url>/l/\?uddg=[^""']+|https?://[^""']+)[""'][^>]*>(?<title>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                foreach (Match m in matches)
                {
                    string rawUrl = m.Groups["url"].Value;
                    string title = Regex.Replace(m.Groups["title"].Value, "<.*?>", " ").Trim();

                    if (rawUrl.Contains("uddg="))
                    {
                        var matchUrl = Regex.Match(rawUrl, @"uddg=(?<target>[^&]+)");
                        if (matchUrl.Success) rawUrl = Uri.UnescapeDataString(matchUrl.Groups["target"].Value);
                    }

                    if (rawUrl.StartsWith("http") && IsUrlSafe(rawUrl) && !list.Any(x => x.Url == rawUrl))
                    {
                        if (!string.IsNullOrWhiteSpace(filterDomain) && !rawUrl.Contains(filterDomain, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string siteName = filterDomain ?? "Web Reference Source";
                        if (rawUrl.Contains("gsmarena.com")) siteName = "GSMArena";
                        else if (rawUrl.Contains("91mobiles.com")) siteName = "91Mobiles";
                        else if (rawUrl.Contains("devicespecifications.com")) siteName = "DeviceSpecifications";
                        else if (rawUrl.Contains("gadgets360.com")) siteName = "NDTV Gadgets360";

                        list.Add(new SpecSourceItem
                        {
                            SiteName = siteName,
                            PageTitle = string.IsNullOrWhiteSpace(title) ? "Mobile Specification Page" : title,
                            Url = rawUrl,
                            SourceType = !string.IsNullOrWhiteSpace(filterDomain) ? "Official Manufacturer" : "Third-Party",
                            RetrievedDate = DateTime.UtcNow
                        });
                    }
                }
            }
            catch
            {
                // Return discovered items
            }
            return list;
        }

        private async Task<bool> FetchAndExtractFromUrlAsync(string url, MobileSpecSearchResultDto result, bool isOfficial, CancellationToken ct)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                var resp = await _httpClient.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) return false;

                string html = await resp.Content.ReadAsStringAsync();
                if (html.Length > 2_000_000) html = html.Substring(0, 2_000_000); // 2MB size cap

                // Detect JS-rendered single page applications
                string plainText = Regex.Replace(html, "<.*?>", " ");
                if (plainText.Length < 300 || html.Contains("JavaScript is required") || html.Contains("Enable JavaScript"))
                {
                    result.RequiresBrowserRendering = true;
                }

                // Exact Model Verification
                if (!string.IsNullOrWhiteSpace(result.ModelName) && !IsExactModelMatch(result.ModelName, html))
                {
                    result.ExactModelMatched = false;
                    if (isOfficial) return false; // Reject mismatched model pages for official searches
                }
                else
                {
                    result.ExactModelMatched = true;
                }

                // Multi-Strategy Extraction across all 44 fields
                ExtractMultiStrategy(html, result.ExtractedSpecs, result.Brand, result.ModelName);

                // Compute statistics
                ComputeStatistics(result, isOfficial ? "Official" : "Third-Party");

                // Record Source Link
                string siteName = isOfficial ? $"{result.Brand} Official Website" : "Reference Website";
                if (url.Contains("gsmarena.com")) siteName = "GSMArena";
                else if (url.Contains("91mobiles.com")) siteName = "91Mobiles";
                else if (url.Contains("devicespecifications.com")) siteName = "DeviceSpecifications";

                result.SourceWebsites.Add(new SpecSourceItem
                {
                    SiteName = siteName,
                    PageTitle = $"{result.Brand} {result.ModelName} Specifications Page",
                    Url = url,
                    SourceType = isOfficial ? "Official Manufacturer" : "Third-Party",
                    RetrievedDate = DateTime.UtcNow
                });

                return result.PopulatedFieldsCount > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool IsExactModelMatch(string modelNameRequested, string html)
        {
            if (string.IsNullOrWhiteSpace(modelNameRequested)) return true;

            string clean = Regex.Replace(html, "<.*?>", " ");
            modelNameRequested = modelNameRequested.Trim();

            if (!clean.Contains(modelNameRequested, StringComparison.OrdinalIgnoreCase)) return false;

            string lowerModel = modelNameRequested.ToLowerInvariant();
            if (!lowerModel.Contains("pro") && Regex.IsMatch(clean, Regex.Escape(modelNameRequested) + @"\s+pro\b", RegexOptions.IgnoreCase))
            {
                int baseCount = Regex.Matches(clean, Regex.Escape(modelNameRequested), RegexOptions.IgnoreCase).Count;
                int proCount = Regex.Matches(clean, Regex.Escape(modelNameRequested) + @"\s+pro\b", RegexOptions.IgnoreCase).Count;
                if (proCount > (baseCount / 2)) return false;
            }

            return true;
        }

        private void ExtractMultiStrategy(string html, MobileSpecifications specs, string brand, string modelName)
        {
            // Strategy A: JSON-LD Structured Microdata
            ExtractJsonLd(html, specs);

            // Strategy B: Robust HTML Table Cell Extraction (GSMArena, 91mobiles, DeviceSpecifications, Official tables)
            ExtractHtmlTablesRobust(html, specs);

            // Strategy C: Definition Lists <dl> <dt> <dd>
            ExtractDefinitionLists(html, specs);

            // Strategy D: Spec Cards <div><span>Key</span><span>Val</span></div>
            ExtractSpecCards(html, specs);

            // Strategy E: Text Regex Fallback for remaining empty properties
            ExtractTextRegex(html, specs, brand);

            // If Apple device or iOS, RAM capacity and RAM Type are not advertised or used by Apple
            if (brand.Contains("Apple", StringComparison.OrdinalIgnoreCase) ||
                modelName.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
                modelName.Contains("iPad", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(specs.OperatingSystem) && (specs.OperatingSystem.Contains("iOS", StringComparison.OrdinalIgnoreCase) || specs.OperatingSystem.Contains("iPadOS", StringComparison.OrdinalIgnoreCase))))
            {
                specs.RamType = string.Empty;
            }
        }

        private void ExtractJsonLd(string html, MobileSpecifications specs)
        {
            try
            {
                var matches = Regex.Matches(html, @"<script[^>]+type=[""']application/ld\+json[""'][^>]*>(?<json>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match m in matches)
                {
                    string jsonStr = m.Groups["json"].Value.Trim();
                    if (string.IsNullOrWhiteSpace(jsonStr)) continue;

                    using var doc = JsonDocument.Parse(jsonStr);
                    var root = doc.RootElement;

                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        ParseJsonLdNode(root, specs);
                    }
                    else if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in root.EnumerateArray())
                        {
                            ParseJsonLdNode(el, specs);
                        }
                    }
                }
            }
            catch { }
        }

        private void ParseJsonLdNode(JsonElement el, MobileSpecifications specs)
        {
            if (el.TryGetProperty("description", out var desc)) MapKeyValueToProperty("description", desc.GetString() ?? "", specs);
            if (el.TryGetProperty("color", out var color)) MapKeyValueToProperty("color", color.GetString() ?? "", specs);
            if (el.TryGetProperty("weight", out var weight)) MapKeyValueToProperty("weight", weight.GetString() ?? "", specs);

            if (el.TryGetProperty("additionalProperty", out var addProp) && addProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var prop in addProp.EnumerateArray())
                {
                    string name = prop.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string val = prop.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(val))
                    {
                        MapKeyValueToProperty(name, val, specs);
                    }
                }
            }
        }

        private void ExtractHtmlTablesRobust(string html, MobileSpecifications specs)
        {
            // Find all <tr> tags and parse cells inside
            var trMatches = Regex.Matches(html, @"<tr[^>]*>(?<content>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            string currentCategoryHeader = "";

            foreach (Match trMatch in trMatches)
            {
                string trContent = trMatch.Groups["content"].Value;

                // Extract all <th> or <td> cells in row
                var cellMatches = Regex.Matches(trContent, @"<t[dh][^>]*>(?<cell>.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (cellMatches.Count == 0) continue;

                var cells = cellMatches.Cast<Match>()
                    .Select(m => Regex.Replace(m.Groups["cell"].Value, "<.*?>", " ").Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                if (cells.Count == 1)
                {
                    currentCategoryHeader = cells[0];
                }
                else if (cells.Count == 2)
                {
                    string key = cells[0];
                    string val = cells[1];
                    MapKeyValueToProperty(key, val, specs, currentCategoryHeader);
                }
                else if (cells.Count >= 3)
                {
                    // GSMArena format: cell[0]=Section (e.g. Display), cell[1]=Key (e.g. Type), cell[2]=Value
                    string section = cells[0];
                    string key = cells[1];
                    string val = cells[2];
                    MapKeyValueToProperty(key, val, specs, section);
                }
            }
        }

        private void ExtractDefinitionLists(string html, MobileSpecifications specs)
        {
            var matches = Regex.Matches(html, @"<dt[^>]*>(?<key>.*?)</dt>\s*<dd[^>]*>(?<val>.*?)</dd>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in matches)
            {
                string key = Regex.Replace(m.Groups["key"].Value, "<.*?>", " ").Trim();
                string val = Regex.Replace(m.Groups["val"].Value, "<.*?>", " ").Trim();
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(val))
                {
                    MapKeyValueToProperty(key, val, specs);
                }
            }
        }

        private void ExtractSpecCards(string html, MobileSpecifications specs)
        {
            var matches = Regex.Matches(html, @"<div[^>]*class=[""'][^""']*(?:label|title|spec-name|spec-lbl)[^""']*[""'][^>]*>(?<key>.*?)</div>\s*<div[^>]*class=[""'][^""']*(?:value|spec-val|spec-value)[^""']*[""'][^>]*>(?<val>.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in matches)
            {
                string key = Regex.Replace(m.Groups["key"].Value, "<.*?>", " ").Trim();
                string val = Regex.Replace(m.Groups["val"].Value, "<.*?>", " ").Trim();
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(val))
                {
                    MapKeyValueToProperty(key, val, specs);
                }
            }
        }

        private void ExtractTextRegex(string html, MobileSpecifications specs, string brand)
        {
            string cleanText = Regex.Replace(html, "<.*?>", " ");

            // Processor Name & Brand
            if (string.IsNullOrWhiteSpace(specs.ProcessorName))
            {
                var m = Regex.Match(cleanText, @"(Snapdragon\s+\d[\d\w\s+]+|A\d+\s+Bionic|A\d+\s+Pro|Dimensity\s+\d+|Exynos\s+\d+|Tensor\s+G\d+|Unisoc\s+T\d+)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    specs.ProcessorName = m.Value.Trim();
                    if (specs.ProcessorName.Contains("Snapdragon")) specs.ProcessorBrand = "Qualcomm";
                    else if (specs.ProcessorName.StartsWith("A1") || specs.ProcessorName.StartsWith("A2")) specs.ProcessorBrand = "Apple";
                    else if (specs.ProcessorName.Contains("Dimensity")) specs.ProcessorBrand = "MediaTek";
                    else if (specs.ProcessorName.Contains("Exynos")) specs.ProcessorBrand = "Samsung";
                    else if (specs.ProcessorName.Contains("Tensor")) specs.ProcessorBrand = "Google";
                    else if (specs.ProcessorName.Contains("Unisoc")) specs.ProcessorBrand = "Unisoc";
                }
            }

            // CPU Cores
            if (string.IsNullOrWhiteSpace(specs.CpuCores))
            {
                var m = Regex.Match(cleanText, @"(Octa-core|Hexa-core|Quad-core|8-core|6-core)", RegexOptions.IgnoreCase);
                if (m.Success) specs.CpuCores = m.Value;
            }

            // Display Size
            if (string.IsNullOrWhiteSpace(specs.DisplaySize))
            {
                var m = Regex.Match(cleanText, @"(\d\.\d{1,2})\s*(?:inch|inches|""|\s*in\b)", RegexOptions.IgnoreCase);
                if (m.Success) specs.DisplaySize = $"{m.Groups[1].Value} inches";
            }

            // Display Type
            if (string.IsNullOrWhiteSpace(specs.DisplayType))
            {
                if (Regex.IsMatch(cleanText, @"Dynamic AMOLED 2X", RegexOptions.IgnoreCase)) specs.DisplayType = "Dynamic AMOLED 2X";
                else if (Regex.IsMatch(cleanText, @"Super Retina XDR", RegexOptions.IgnoreCase)) specs.DisplayType = "Super Retina XDR OLED";
                else if (Regex.IsMatch(cleanText, @"AMOLED", RegexOptions.IgnoreCase)) specs.DisplayType = "AMOLED";
                else if (Regex.IsMatch(cleanText, @"OLED", RegexOptions.IgnoreCase)) specs.DisplayType = "OLED";
                else if (Regex.IsMatch(cleanText, @"IPS LCD", RegexOptions.IgnoreCase)) specs.DisplayType = "IPS LCD";
            }

            // Refresh Rate
            if (string.IsNullOrWhiteSpace(specs.RefreshRate))
            {
                var m = Regex.Match(cleanText, @"(144Hz|120Hz|90Hz|165Hz)", RegexOptions.IgnoreCase);
                if (m.Success) specs.RefreshRate = m.Value;
            }

            // Battery Capacity
            if (specs.BatteryCapacityMah <= 0 || specs.BatteryCapacityMah == 5000)
            {
                var m = Regex.Match(cleanText, @"(\d{4,5})\s*mAh", RegexOptions.IgnoreCase);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int mah)) specs.BatteryCapacityMah = mah;
            }

            // Fast Charging Wattage
            if (string.IsNullOrWhiteSpace(specs.FastChargingWattage))
            {
                var m = Regex.Match(cleanText, @"(\d{2,3}W)\s*(?:fast|charging|wired|FlashCharge|SuperVOOC)", RegexOptions.IgnoreCase);
                if (m.Success) specs.FastChargingWattage = $"{m.Groups[1].Value} Fast Charging";
            }

            // Primary Rear Camera MP
            if (string.IsNullOrWhiteSpace(specs.PrimaryRearCameraMp))
            {
                var m = Regex.Match(cleanText, @"(\d{2,3}\s*MP(?:\s*\+\s*\d{1,3}\s*MP)*)", RegexOptions.IgnoreCase);
                if (m.Success) specs.PrimaryRearCameraMp = m.Value;
            }

            // Front Camera MP
            if (string.IsNullOrWhiteSpace(specs.FrontCameraMp))
            {
                var m = Regex.Match(cleanText, @"(?:front|selfie)[^.\n]*?(\d{1,2}\s*MP)", RegexOptions.IgnoreCase);
                if (m.Success) specs.FrontCameraMp = m.Groups[1].Value;
            }

            // Network 5G
            if (Regex.IsMatch(cleanText, @"\b5G\b", RegexOptions.IgnoreCase)) specs.Network5G = true;

            // NFC
            if (Regex.IsMatch(cleanText, @"\bNFC\b", RegexOptions.IgnoreCase)) specs.Nfc = true;

            // Water Resistance
            if (string.IsNullOrWhiteSpace(specs.WaterResistance))
            {
                var m = Regex.Match(cleanText, @"\b(IP68|IP67|IP69|IP54)\b", RegexOptions.IgnoreCase);
                if (m.Success) specs.WaterResistance = m.Value.ToUpperInvariant();
            }

            // Official Colors
            if (string.IsNullOrWhiteSpace(specs.OfficialColors))
            {
                var m = Regex.Match(cleanText, @"(?:colors?|colours?)\s*:?\s*([A-Za-z\s,]+(?:Black|White|Blue|Gray|Grey|Green|Silver|Gold|Titanium)[A-Za-z\s,]*)", RegexOptions.IgnoreCase);
                if (m.Success) specs.OfficialColors = m.Groups[1].Value.Trim();
            }
        }

        private void MapKeyValueToProperty(string key, string value, MobileSpecifications specs, string contextCategory = "")
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return;

            string k = key.ToLowerInvariant().Trim();
            string v = value.Trim();
            string category = contextCategory.ToLowerInvariant().Trim();

            // Processor & Performance
            if (k.Contains("chipset") || k.Contains("soc") || k.Contains("processor") || category.Contains("platform"))
            {
                if (k.Contains("brand") && string.IsNullOrWhiteSpace(specs.ProcessorBrand)) specs.ProcessorBrand = v;
                else if (string.IsNullOrWhiteSpace(specs.ProcessorName)) specs.ProcessorName = v;
                else if (string.IsNullOrWhiteSpace(specs.Chipset)) specs.Chipset = v;
            }
            if (k.Contains("cpu") || k.Contains("cores") || k.Contains("architecture"))
            {
                if (string.IsNullOrWhiteSpace(specs.CpuCores)) specs.CpuCores = v;
            }
            if (k.Contains("gpu") || k.Contains("graphics"))
            {
                if (string.IsNullOrWhiteSpace(specs.Gpu)) specs.Gpu = v;
            }

            // Memory & Storage
            if (k.Contains("internal") || k.Contains("memory") || category.Contains("memory"))
            {
                bool isApple = (!string.IsNullOrWhiteSpace(specs.OperatingSystem) && (specs.OperatingSystem.Contains("iOS", StringComparison.OrdinalIgnoreCase) || specs.OperatingSystem.Contains("iPadOS", StringComparison.OrdinalIgnoreCase)))
                            || (!string.IsNullOrWhiteSpace(specs.ProcessorBrand) && specs.ProcessorBrand.Contains("Apple", StringComparison.OrdinalIgnoreCase));
                if (!isApple && (k.Contains("ram") || v.Contains("RAM")) && string.IsNullOrWhiteSpace(specs.RamType))
                {
                    specs.RamType = v;
                }
                if ((k.Contains("storage") || k.Contains("ufs") || v.Contains("UFS")) && string.IsNullOrWhiteSpace(specs.StorageType)) specs.StorageType = v;
            }
            if (k.Contains("card slot") || k.Contains("expandable"))
            {
                specs.ExpandableStorage = !v.Contains("No", StringComparison.OrdinalIgnoreCase) && !v.Contains("Unspecified", StringComparison.OrdinalIgnoreCase);
                if (specs.ExpandableStorage && string.IsNullOrWhiteSpace(specs.MaxExpandableStorage)) specs.MaxExpandableStorage = v;
            }

            // Display
            if (k.Contains("display") || k.Contains("screen") || category.Contains("display"))
            {
                if ((k.Contains("size") || k.Equals("display")) && string.IsNullOrWhiteSpace(specs.DisplaySize)) specs.DisplaySize = v;
                else if ((k.Contains("type") || k.Contains("panel")) && string.IsNullOrWhiteSpace(specs.DisplayType)) specs.DisplayType = v;
                else if (k.Contains("resolution") && string.IsNullOrWhiteSpace(specs.Resolution)) specs.Resolution = v;
                else if (k.Contains("protection") && string.IsNullOrWhiteSpace(specs.ScreenProtection)) specs.ScreenProtection = v;
            }
            if (k.Contains("refresh rate") || v.Contains("Hz"))
            {
                if (string.IsNullOrWhiteSpace(specs.RefreshRate)) specs.RefreshRate = v;
            }

            // Rear & Front Camera
            if (category.Contains("main camera") || category.Contains("camera") || k.Contains("camera"))
            {
                if ((k.Contains("single") || k.Contains("dual") || k.Contains("triple") || k.Contains("quad") || k.Contains("primary") || k.Contains("modules")) && string.IsNullOrWhiteSpace(specs.PrimaryRearCameraMp))
                {
                    specs.PrimaryRearCameraMp = v;
                }
                else if (k.Contains("ultrawide") || k.Contains("ultra-wide")) { if (string.IsNullOrWhiteSpace(specs.UltrawideCameraMp)) specs.UltrawideCameraMp = v; }
                else if (k.Contains("telephoto") || k.Contains("periscope")) { if (string.IsNullOrWhiteSpace(specs.TelephotoCameraMp)) specs.TelephotoCameraMp = v; }
                else if (k.Contains("selfie") || k.Contains("front")) { if (string.IsNullOrWhiteSpace(specs.FrontCameraMp)) specs.FrontCameraMp = v; }
                else if (k.Contains("features") || k.Contains("video")) { if (string.IsNullOrWhiteSpace(specs.CameraFeatures)) specs.CameraFeatures = v; }
            }

            // Battery & Charging
            if (k.Contains("battery") || category.Contains("battery"))
            {
                if (k.Contains("type") || k.Contains("capacity") || k.Equals("battery") || v.Contains("mAh"))
                {
                    var m = Regex.Match(v, @"(\d{4,5})");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out int mah)) specs.BatteryCapacityMah = mah;
                }
                if (k.Contains("charging") || k.Contains("speed") || k.Contains("fast"))
                {
                    if (string.IsNullOrWhiteSpace(specs.FastChargingWattage)) specs.FastChargingWattage = v;
                }
            }

            // Connectivity & Network
            if (k.Contains("technology") || k.Contains("network") || category.Contains("net"))
            {
                if (v.Contains("5G", StringComparison.OrdinalIgnoreCase)) specs.Network5G = true;
                if (v.Contains("LTE", StringComparison.OrdinalIgnoreCase) || v.Contains("4G", StringComparison.OrdinalIgnoreCase)) specs.Network4G = true;
            }
            if (k.Contains("wlan") || k.Contains("wifi") || k.Contains("wi-fi")) { if (string.IsNullOrWhiteSpace(specs.WifiVersion)) specs.WifiVersion = v; }
            if (k.Contains("bluetooth")) { if (string.IsNullOrWhiteSpace(specs.BluetoothVersion)) specs.BluetoothVersion = v; }
            if (k.Contains("nfc")) { specs.Nfc = !v.Contains("No", StringComparison.OrdinalIgnoreCase); }
            if (k.Contains("sim")) { if (string.IsNullOrWhiteSpace(specs.SimType)) specs.SimType = v; }

            // OS & Security
            if (k.Contains("os") || k.Contains("operating system") || category.Contains("platform"))
            {
                if (string.IsNullOrWhiteSpace(specs.OperatingSystem)) specs.OperatingSystem = v;
            }
            if (k.Contains("sensors") || k.Contains("fingerprint"))
            {
                if (v.Contains("fingerprint", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(specs.FingerprintSensor)) specs.FingerprintSensor = v;
                if (string.IsNullOrWhiteSpace(specs.Sensors)) specs.Sensors = v;
            }

            // Physical Specs
            if (k.Contains("colors") || k.Contains("colours")) { if (string.IsNullOrWhiteSpace(specs.OfficialColors)) specs.OfficialColors = v; }
            if (k.Contains("dimensions") || k.Contains("weight") || k.Contains("body")) { if (string.IsNullOrWhiteSpace(specs.DimensionsWeight)) specs.DimensionsWeight = v; }
        }

        private void ComputeStatistics(MobileSpecSearchResultDto result, string defaultTag)
        {
            var specs = result.ExtractedSpecs;
            int total = SpecProperties.Length;
            int populated = 0;
            int officialCount = 0;
            int thirdPartyCount = 0;
            int unavailableCount = 0;

            foreach (var prop in SpecProperties)
            {
                object? val = prop.GetValue(specs);
                bool hasVal = false;

                if (val is string str)
                {
                    hasVal = !string.IsNullOrWhiteSpace(str) && !str.Equals("Not Available", StringComparison.OrdinalIgnoreCase);
                }
                else if (val is int num)
                {
                    hasVal = num > 0;
                }
                else if (val is bool b)
                {
                    hasVal = b; // Boolean property populated
                }

                string pName = prop.Name;
                if (hasVal)
                {
                    populated++;
                    result.FieldSources[pName] = defaultTag;
                    if (defaultTag == "Official") officialCount++;
                    else if (defaultTag == "Third-Party") thirdPartyCount++;
                }
                else
                {
                    result.FieldSources[pName] = "Not Available";
                    unavailableCount++;
                }
            }

            result.TotalFieldsCount = total;
            result.PopulatedFieldsCount = populated;
            result.OfficialFieldsCount = officialCount;
            result.ThirdPartyFieldsCount = thirdPartyCount;
            result.UnavailableFieldsCount = unavailableCount;
        }

        private bool IsUrlSafe(string urlString)
        {
            if (string.IsNullOrWhiteSpace(urlString)) return false;
            if (!Uri.TryCreate(urlString, UriKind.Absolute, out Uri? uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

            string host = uri.Host.ToLowerInvariant();
            if (host == "localhost" || host == "127.0.0.1" || host == "0.0.0.0" || host == "::1") return false;

            if (IPAddress.TryParse(host, out IPAddress? ip))
            {
                byte[] bytes = ip.GetAddressBytes();
                if (bytes[0] == 10) return false;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
                if (bytes[0] == 192 && bytes[1] == 168) return false;
                if (bytes[0] == 169 && bytes[1] == 254) return false;
            }

            return true;
        }
    }
}
