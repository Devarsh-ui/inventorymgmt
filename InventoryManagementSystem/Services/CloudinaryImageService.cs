using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using InventoryManagementSystem.Configuration;
using InventoryManagementSystem.DTOs;
using ImageUploadResult = InventoryManagementSystem.DTOs.ImageUploadResult;
using InventoryManagementSystem.Interfaces;
using System;
using System.IO;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Services
{
    public class CloudinaryImageService : IImageService
    {
        private readonly Cloudinary? _cloudinary;
        private readonly CloudinarySettings _config;

        public CloudinaryImageService(IOptions<CloudinarySettings> config)
        {
            _config = config.Value ?? new CloudinarySettings();
            if (!string.IsNullOrWhiteSpace(_config.CloudName) && !string.IsNullOrWhiteSpace(_config.ApiKey) && !string.IsNullOrWhiteSpace(_config.ApiSecret))
            {
                var acc = new Account(
                    _config.CloudName,
                    _config.ApiKey,
                    _config.ApiSecret
                );
                _cloudinary = new Cloudinary(acc);
            }
        }

        public async Task<ImageUploadResult> UploadImageAsync(IFormFile file, string folder)
        {
            var result = new ImageUploadResult();

            if (file == null || file.Length == 0)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "File is empty.";
                return result;
            }

            var extension = Path.GetExtension(file.FileName).ToLower();
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            if (Array.IndexOf(allowedExtensions, extension) < 0)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Only JPG, JPEG, PNG, and WEBP files are allowed.";
                return result;
            }

            if (file.Length > 15 * 1024 * 1024) // 15MB limit
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Maximum allowed image size is 15MB.";
                return result;
            }

            // If Cloudinary is not configured, fall back to local disk storage
            if (_cloudinary == null)
            {
                return await SaveLocallyAsync(file, folder, result);
            }

            try
            {
                using var stream = file.OpenReadStream();
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folder
                };

                var uploadResult = await _cloudinary.UploadAsync(uploadParams);

                if (uploadResult.Error != null)
                {
                    // Fall back to local storage if Cloudinary returns an error
                    return await SaveLocallyAsync(file, folder, result);
                }

                result.IsSuccess = true;
                result.PublicId = uploadResult.PublicId;
                result.SecureUrl = uploadResult.SecureUrl.ToString();
                result.OriginalFilename = file.FileName;
                return result;
            }
            catch
            {
                // Fall back to local disk storage on exception
                return await SaveLocallyAsync(file, folder, result);
            }
        }

        private async Task<ImageUploadResult> SaveLocallyAsync(IFormFile file, string folder, ImageUploadResult result)
        {
            try
            {
                var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var targetDir = Path.Combine(webRoot, "uploads", folder);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                var ext = Path.GetExtension(file.FileName).ToLower();
                var safeName = $"{Guid.NewGuid():N}{ext}";
                var filePath = Path.Combine(targetDir, safeName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fileStream);
                }

                result.IsSuccess = true;
                result.PublicId = $"local_{safeName}";
                result.SecureUrl = $"/uploads/{folder}/{safeName}";
                result.OriginalFilename = file.FileName;
                return result;
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"Failed to save image: {ex.Message}";
                return result;
            }
        }

        public async Task<bool> DeleteImageAsync(string publicId)
        {
            if (string.IsNullOrEmpty(publicId))
                return false;

            if (publicId.StartsWith("local_"))
            {
                try
                {
                    var fileName = publicId.Substring(6);
                    var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                    if (Directory.Exists(uploadsDir))
                    {
                        var files = Directory.GetFiles(uploadsDir, fileName, SearchOption.AllDirectories);
                        foreach (var f in files)
                        {
                            if (File.Exists(f)) File.Delete(f);
                        }
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            if (_cloudinary == null) return false;

            try
            {
                var deleteParams = new DeletionParams(publicId);
                var result = await _cloudinary.DestroyAsync(deleteParams);
                return result.Result == "ok";
            }
            catch
            {
                return false;
            }
        }

        public async Task<ImageUploadResult> ReplaceImageAsync(string oldPublicId, IFormFile newFile, string folder)
        {
            if (!string.IsNullOrEmpty(oldPublicId))
            {
                await DeleteImageAsync(oldPublicId);
            }
            return await UploadImageAsync(newFile, folder);
        }
    }
}
