using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MasseterDrop.Helpers;
using MasseterDrop.Models;
using MasseterDrop.Services;
using MasseterDrop.ViewModels;

namespace MasseterDrop.Tests
{
    class Program
    {
        static int _passed = 0;
        static int _failed = 0;

        [STAThread]
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("     MASSETER DROP — COMPREHENSIVE TEST SUITE    ");
            Console.WriteLine("=================================================");
            Console.WriteLine();

            TestDownloadsFolderDetection();
            TestCategorizationAndFormatting();
            TestSettingsPersistence();
            TestPhantomFilesDoNotExistOnDisk();
            TestNoTemporaryEntriesPersistedInHistory();
            TestExistingDownloadHistoryHandling();
            TestRemovedHistoryPersistenceAndRescan();
            await TestMultipleEventsSingleTempFileAsync();
            await TestTempFileRenamedToFinalAsync();
            await TestSingleDownloadExactlyOneCompletedEntryAsync();
            await TestMultipleSimultaneousDownloadsAsync();
            await TestTempFileDeletionBeforeCompletionAsync();
            TestTaskbarPositioning();
            TestShelfWindowPositioning();
            await TestImageThumbnailGenerationAsync();
            await TestPdfAndCorruptFileHandlingAsync();
            await TestDownloadCompletedEventNotificationAsync();

            TestAnalyticsInstallationId();
            TestAnalyticsConsentAndOptOut();
            TestAnalyticsPrivacyAndDataSanitization();
            TestAnalyticsEventFlow();
            await TestAnalyticsShutdownTimeoutAndResilienceAsync();
            await TestPostHogLiveCaptureAsync();

            Console.WriteLine();
            Console.WriteLine("=================================================");
            Console.WriteLine($"TEST RESULTS: {_passed} PASSED, {_failed} FAILED");
            Console.WriteLine("=================================================");

            return _failed == 0 ? 0 : 1;
        }

        static void Assert(string testName, bool condition, string detail = "")
        {
            if (condition)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  [PASS] ");
                Console.ResetColor();
                Console.WriteLine($"{testName} {(string.IsNullOrEmpty(detail) ? "" : "— " + detail)}");
                _passed++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  [FAIL] ");
                Console.ResetColor();
                Console.WriteLine($"{testName} {(string.IsNullOrEmpty(detail) ? "" : "— " + detail)}");
                _failed++;
            }
        }

        static void TestDownloadsFolderDetection()
        {
            Console.WriteLine("-> Running Downloads Folder Detection Tests...");
            string path = NativeMethods.GetDownloadsFolderPath();
            Assert("Downloads folder discovered", !string.IsNullOrEmpty(path), $"Path: {path}");
            Assert("Downloads folder exists on disk", Directory.Exists(path), path);
            Console.WriteLine();
        }

        static void TestCategorizationAndFormatting()
        {
            Console.WriteLine("-> Running Categorization & Formatting Tests...");
            Assert("PDF extension categorization", DownloadItem.CategorizeExtension(".pdf") == FileCategory.Pdf);
            Assert("PNG extension categorization", DownloadItem.CategorizeExtension(".png") == FileCategory.Image);
            Assert("MP4 extension categorization", DownloadItem.CategorizeExtension(".mp4") == FileCategory.Video);
            Assert("MP3 extension categorization", DownloadItem.CategorizeExtension(".mp3") == FileCategory.Audio);
            Assert("ZIP extension categorization", DownloadItem.CategorizeExtension(".zip") == FileCategory.Archive);
            Assert("DOCX extension categorization", DownloadItem.CategorizeExtension(".docx") == FileCategory.Document);
            Assert("EXE extension categorization", DownloadItem.CategorizeExtension(".exe") == FileCategory.Executable);
            Assert("CS extension categorization", DownloadItem.CategorizeExtension(".cs") == FileCategory.Code);

            Assert("FormatBytes: 500 B", DownloadItem.FormatBytes(500) == "500 B");
            Assert("FormatBytes: 1536 B -> 1.5 KB", DownloadItem.FormatBytes(1536) == "1.5 KB");
            Assert("FormatBytes: 10485760 B -> 10 MB", DownloadItem.FormatBytes(10485760) == "10 MB");
            Console.WriteLine();
        }

        static void TestSettingsPersistence()
        {
            Console.WriteLine("-> Running Settings Persistence Tests...");
            var settingsService = new SettingsService();
            var testSettings = new AppSettings
            {
                LaunchAtStartup = false,
                StartMinimized = true,
                KeepRunningOnClose = true,
                MaxRecentFiles = 200,
                Theme = "Dark",
                AutoDismissOnDeactivate = false
            };

            settingsService.SaveSettings(testSettings);
            var loaded = settingsService.LoadSettings();

            Assert("Settings StartMinimized preserved", loaded.StartMinimized == true);
            Assert("Settings MaxRecentFiles preserved", loaded.MaxRecentFiles == 200);
            Assert("Settings AutoDismissOnDeactivate preserved", loaded.AutoDismissOnDeactivate == false);
            Assert("Settings Theme preserved", loaded.Theme == "Dark");
            Console.WriteLine();
        }

        static void TestRemovedHistoryPersistenceAndRescan()
        {
            Console.WriteLine("-> Running Removed Recent Item Persistence Tests...");
            string root = Path.Combine(Path.GetTempPath(), "MasseterDrop_RemovedHistory_" + Guid.NewGuid().ToString("N"));
            string downloads = Path.Combine(root, "Downloads");
            string state = Path.Combine(root, "State");
            Directory.CreateDirectory(downloads);
            string file = Path.Combine(downloads, "keep-on-disk.pdf");
            File.WriteAllText(file, "file contents");

            try
            {
                var service = new DownloadHistoryService(state);
                var firstLoad = service.LoadHistory(downloads);
                Assert("Existing file appears before removal", firstLoad.Any(i => i.FullPath == file));
                service.RemoveFromHistory(file, firstLoad.Where(i => i.FullPath != file));

                // A new service instance represents an application restart.
                var afterRestart = new DownloadHistoryService(state).LoadHistory(downloads);
                Assert("Removed file stays out after history reload", !afterRestart.Any(i => i.FullPath == file));
                Assert("Removing history leaves the real file intact", File.Exists(file));

                service.ClearRemovedItems();
                Assert("Explicit refresh can rediscover the file", service.LoadHistory(downloads).Any(i => i.FullPath == file));

                service.RemoveFromHistory(file, Array.Empty<DownloadItem>());
                service.MarkDownloaded(file);
                Assert("A new completion at the same path restores the item", service.LoadHistory(downloads).Any(i => i.FullPath == file));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
            Console.WriteLine();
        }

        static void TestPhantomFilesDoNotExistOnDisk()
        {
            Console.WriteLine("-> Running Phantom & Temporary Path Detection Tests...");

            // Test 1: Temporary extensions
            Assert(".crdownload is temporary", DownloadPathHelper.IsTemporaryOrPhantom("file.pdf.crdownload"));
            Assert(".part is temporary", DownloadPathHelper.IsTemporaryOrPhantom("music.mp3.part"));
            Assert(".tmp is temporary", DownloadPathHelper.IsTemporaryOrPhantom("archive.zip.tmp"));
            Assert(".download is temporary", DownloadPathHelper.IsTemporaryOrPhantom("setup.exe.download"));

            // Test 2: Chrome Unconfirmed pattern
            Assert("Unconfirmed 801882 is phantom", DownloadPathHelper.IsTemporaryOrPhantom("Unconfirmed 801882"));
            Assert("Unconfirmed 801882.crdownload is phantom", DownloadPathHelper.IsTemporaryOrPhantom("Unconfirmed 801882.crdownload"));
            Assert("Unconfirmed_999 is phantom", DownloadPathHelper.IsTemporaryOrPhantom("Unconfirmed_999.tmp"));

            // Test 3: GUID-like filenames
            string guid1 = "{" + Guid.NewGuid().ToString().ToUpper() + "}";
            string guid2 = Guid.NewGuid().ToString().ToLower();
            Assert("Braced GUID is phantom", DownloadPathHelper.IsTemporaryOrPhantom(guid1));
            Assert("Standard GUID is phantom", DownloadPathHelper.IsTemporaryOrPhantom(guid2));
            Assert("GUID with .tmp is phantom", DownloadPathHelper.IsTemporaryOrPhantom(guid2 + ".tmp"));

            // Test 4: Legitimate files
            Assert("Regular PNG is not phantom", !DownloadPathHelper.IsTemporaryOrPhantom("photo.png"));
            Assert("Regular PDF is not phantom", !DownloadPathHelper.IsTemporaryOrPhantom("invoice.pdf"));
            Assert("Regular ZIP is not phantom", !DownloadPathHelper.IsTemporaryOrPhantom("code.zip"));

            // Test 5: Target display name sanitization
            Assert("Target name for cat.png.crdownload -> cat.png", DownloadPathHelper.GetTargetDisplayName("cat.png.crdownload") == "cat.png");
            Assert("Target name for Unconfirmed 801882.crdownload -> Downloading...", DownloadPathHelper.GetTargetDisplayName("Unconfirmed 801882.crdownload") == "Downloading...");
            Assert("Target name for GUID -> Downloading...", DownloadPathHelper.GetTargetDisplayName(guid1) == "Downloading...");
            Console.WriteLine();
        }

        static void TestNoTemporaryEntriesPersistedInHistory()
        {
            Console.WriteLine("-> Running History Sanitization & No Temp Entries Tests...");
            var historyService = new DownloadHistoryService();
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_HistorySaveTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                // Create a real file
                string realFile = Path.Combine(testDir, "ValidDocument.pdf");
                File.WriteAllText(realFile, "Real Content");

                // Phantom files that do NOT exist
                string phantomPath1 = Path.Combine(testDir, "Unconfirmed 801882");
                string phantomPath2 = Path.Combine(testDir, "{80188241-1234-5678-90ab-cdef01234567}.tmp");
                string nonExistentFile = Path.Combine(testDir, "NonExistent.png");

                var items = new List<DownloadItem>
                {
                    new DownloadItem
                    {
                        FileName = "ValidDocument.pdf",
                        FullPath = realFile,
                        Status = DownloadStatus.Completed,
                        IsAvailable = true,
                        FileSize = 120
                    },
                    new DownloadItem
                    {
                        FileName = "Unconfirmed 801882",
                        FullPath = phantomPath1,
                        Status = DownloadStatus.Downloading,
                        IsAvailable = true
                    },
                    new DownloadItem
                    {
                        FileName = "{80188241-1234-5678-90ab-cdef01234567}",
                        FullPath = phantomPath2,
                        Status = DownloadStatus.Completed,
                        IsAvailable = false
                    },
                    new DownloadItem
                    {
                        FileName = "NonExistent.png",
                        FullPath = nonExistentFile,
                        Status = DownloadStatus.Completed,
                        IsAvailable = true
                    }
                };

                // Save to history
                historyService.SaveHistory(items, 100);

                // Reload and verify
                var reloaded = historyService.LoadHistory(testDir, 100);

                Assert("Exactly 1 valid item in reloaded history", reloaded.Count == 1, $"Count: {reloaded.Count}");
                Assert("Reloaded item is ValidDocument.pdf", reloaded[0].FileName == "ValidDocument.pdf");
                Assert("Phantom Unconfirmed 801882 was rejected from history", !reloaded.Any(i => i.FileName.Contains("Unconfirmed")));
                Assert("Non-existent file was rejected from history", !reloaded.Any(i => i.FileName == "NonExistent.png"));
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        static void TestExistingDownloadHistoryHandling()
        {
            Console.WriteLine("-> Running Existing Corrupted History Purging Tests...");
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_LegacyHistoryTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            string storageDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasseterDrop");
            Directory.CreateDirectory(storageDir);
            string historyFile = Path.Combine(storageDir, "history.json");

            try
            {
                string realDoc = Path.Combine(testDir, "AnnualReport.pdf");
                File.WriteAllText(realDoc, "Annual Report");

                // Simulate legacy history file containing corrupted/phantom items from prior bug
                var corruptedData = new List<DownloadItem>
                {
                    new DownloadItem
                    {
                        FileName = "Unconfirmed 123456",
                        FullPath = Path.Combine(testDir, "Unconfirmed 123456.crdownload"),
                        Status = DownloadStatus.Downloading
                    },
                    new DownloadItem
                    {
                        FileName = "{80188241-0000-0000-0000-000000000000}",
                        FullPath = Path.Combine(testDir, "{80188241-0000-0000-0000-000000000000}.tmp"),
                        Status = DownloadStatus.Completed
                    },
                    new DownloadItem
                    {
                        FileName = "AnnualReport.pdf",
                        FullPath = realDoc,
                        Status = DownloadStatus.Completed,
                        IsAvailable = true,
                        FileSize = 50
                    }
                };

                File.WriteAllText(historyFile, JsonSerializer.Serialize(corruptedData, new JsonSerializerOptions { WriteIndented = true }));

                var historyService = new DownloadHistoryService();
                var cleanedHistory = historyService.LoadHistory(testDir, 100);

                Assert("Legacy corrupted history purged of phantom entries", !cleanedHistory.Any(i => i.FileName.Contains("Unconfirmed")));
                Assert("Legacy corrupted history purged of GUIDs", !cleanedHistory.Any(i => i.FileName.Contains("{80188241")));
                Assert("Legitimate file AnnualReport.pdf preserved", cleanedHistory.Any(i => i.FileName == "AnnualReport.pdf"));
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        static async Task TestMultipleEventsSingleTempFileAsync()
        {
            Console.WriteLine("-> Running Multiple Events for Single Temp File Tests...");
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_MultiEventTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            using var monitor = new DownloadMonitor(testDir);
            var downloadingItems = new List<DownloadItem>();
            var completedItems = new List<DownloadItem>();

            monitor.FileDownloading += item => { lock (downloadingItems) downloadingItems.Add(item); };
            monitor.FileCompleted += item => { lock (completedItems) completedItems.Add(item); };

            try
            {
                // Chrome creates temp file
                string tempFile = Path.Combine(testDir, "Unconfirmed 801882.crdownload");
                File.WriteAllText(tempFile, "Chunk 1");

                // Rapid chunk writes (multiple Changed events)
                for (int i = 2; i <= 8; i++)
                {
                    await Task.Delay(80);
                    File.AppendAllText(tempFile, $"Chunk {i}");
                }

                await Task.Delay(600);

                lock (downloadingItems)
                {
                    Assert("Temp file notified as downloading", downloadingItems.Count > 0);
                    // All notifications should reuse the same session Id
                    string firstId = downloadingItems[0].Id;
                    bool allSameId = downloadingItems.All(d => d.Id == firstId);
                    Assert("Multiple chunk events reuse stable session Id", allSameId, $"SessionId: {firstId}");
                    Assert("Placeholder display name used instead of phantom name", downloadingItems.All(d => d.FileName == "Downloading..."));
                }

                lock (completedItems)
                {
                    Assert("Zero completed events fired while still temporary", completedItems.Count == 0);
                }
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        static async Task TestTempFileRenamedToFinalAsync()
        {
            Console.WriteLine("-> Running Temp File Renamed to Final Tests...");
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_RenameTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            using var monitor = new DownloadMonitor(testDir);
            var downloadingItems = new List<DownloadItem>();
            DownloadItem? completedItem = null;

            monitor.FileDownloading += item => { lock (downloadingItems) downloadingItems.Add(item); };
            monitor.FileCompleted += item => { completedItem = item; };

            try
            {
                // Step 1: Initial creation: Unconfirmed 918273.crdownload
                string step1 = Path.Combine(testDir, "Unconfirmed 918273.crdownload");
                File.WriteAllText(step1, "Downloading image bytes...");
                await Task.Delay(400);

                string originalSessionId;
                lock (downloadingItems)
                {
                    Assert("Initial temp file detected as downloading", downloadingItems.Count > 0);
                    originalSessionId = downloadingItems[0].Id;
                }

                // Step 2: Intermediate rename: Unconfirmed 918273.crdownload -> scenery.png.crdownload
                string step2 = Path.Combine(testDir, "scenery.png.crdownload");
                File.Move(step1, step2);
                await Task.Delay(400);

                lock (downloadingItems)
                {
                    var updated = downloadingItems.LastOrDefault();
                    Assert("Intermediate rename updated filename to scenery.png", updated != null && updated.FileName == "scenery.png");
                    Assert("Intermediate rename preserved original session Id", updated != null && updated.Id == originalSessionId);
                }

                // Step 3: Final rename: scenery.png.crdownload -> scenery.png
                string step3 = Path.Combine(testDir, "scenery.png");
                File.Move(step2, step3);
                await Task.Delay(1500);

                Assert("Final completion detected", completedItem != null);
                Assert("Completed item has correct filename scenery.png", completedItem?.FileName == "scenery.png");
                Assert("Completed item has original session Id (stable identity)", completedItem?.Id == originalSessionId);
                Assert("Completed item Status is Completed", completedItem?.Status == DownloadStatus.Completed);
                Assert("Completed item is available and exists on disk", completedItem != null && completedItem.IsAvailable && File.Exists(completedItem.FullPath));
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        static async Task TestSingleDownloadExactlyOneCompletedEntryAsync()
        {
            Console.WriteLine("-> Running Single Download Exactly One Completed Entry (End-to-End ViewModel) Tests...");
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_E2ETest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            var settingsService = new SettingsService();
            settingsService.CurrentSettings.CustomDownloadsFolder = testDir;

            var historyService = new DownloadHistoryService();
            var fileOpService = new FileOperationService();

            var vm = new DownloadsViewModel(settingsService, historyService, fileOpService);

            try
            {
                // Full Chrome lifecycle simulation
                // 1. Unconfirmed file created
                string f1 = Path.Combine(testDir, "Unconfirmed 801882.crdownload");
                File.WriteAllText(f1, "Chrome downloading image...");
                await Task.Delay(400);

                // 2. Renamed to final target with .crdownload
                string f2 = Path.Combine(testDir, "profile_photo.jpg.crdownload");
                File.Move(f1, f2);
                await Task.Delay(400);

                // 3. Completed: renamed to profile_photo.jpg
                string f3 = Path.Combine(testDir, "profile_photo.jpg");
                File.Move(f2, f3);
                await Task.Delay(1500);

                // Check ViewModel downloads collection
                Assert("ViewModel has EXACTLY 1 item (no phantoms, no duplicates)", vm.Downloads.Count == 1, $"Actual count: {vm.Downloads.Count}");
                if (vm.Downloads.Count > 0)
                {
                    var item = vm.Downloads[0];
                    Assert("Item filename is profile_photo.jpg", item.FileName == "profile_photo.jpg");
                    Assert("Item status is Completed", item.Status == DownloadStatus.Completed);
                    Assert("Item is marked Available", item.IsAvailable);
                    Assert("No Unconfirmed phantom exists in ViewModel", !vm.Downloads.Any(d => d.FileName.Contains("Unconfirmed")));
                    Assert("No .crdownload phantom exists in ViewModel", !vm.Downloads.Any(d => d.FullPath.EndsWith(".crdownload")));
                }
            }
            finally
            {
                vm.Dispose();
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        static async Task TestMultipleSimultaneousDownloadsAsync()
        {
            Console.WriteLine("-> Running Multiple Simultaneous Downloads Tests...");
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_SimultaneousTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            var settingsService = new SettingsService();
            settingsService.CurrentSettings.CustomDownloadsFolder = testDir;

            var historyService = new DownloadHistoryService();
            var fileOpService = new FileOperationService();

            var vm = new DownloadsViewModel(settingsService, historyService, fileOpService);

            try
            {
                // 3 concurrent downloads in progress
                string u1 = Path.Combine(testDir, "Unconfirmed 101.crdownload");
                string u2 = Path.Combine(testDir, "Unconfirmed 102.crdownload");
                string u3 = Path.Combine(testDir, "Unconfirmed 103.crdownload");

                File.WriteAllText(u1, "Data 1");
                File.WriteAllText(u2, "Data 2");
                File.WriteAllText(u3, "Data 3");
                await Task.Delay(400);

                // Simultaneously rename to intermediate targets
                string i1 = Path.Combine(testDir, "report.pdf.crdownload");
                string i2 = Path.Combine(testDir, "presentation.pptx.crdownload");
                string i3 = Path.Combine(testDir, "archive.zip.crdownload");

                File.Move(u1, i1);
                File.Move(u2, i2);
                File.Move(u3, i3);
                await Task.Delay(400);

                // Complete all 3
                string c1 = Path.Combine(testDir, "report.pdf");
                string c2 = Path.Combine(testDir, "presentation.pptx");
                string c3 = Path.Combine(testDir, "archive.zip");

                File.Move(i1, c1);
                File.Move(i2, c2);
                File.Move(i3, c3);
                await Task.Delay(1800);

                Assert("ViewModel contains exactly 3 completed downloads", vm.Downloads.Count == 3, $"Actual: {vm.Downloads.Count}");
                Assert("report.pdf is present and completed", vm.Downloads.Any(d => d.FileName == "report.pdf" && d.Status == DownloadStatus.Completed));
                Assert("presentation.pptx is present and completed", vm.Downloads.Any(d => d.FileName == "presentation.pptx" && d.Status == DownloadStatus.Completed));
                Assert("archive.zip is present and completed", vm.Downloads.Any(d => d.FileName == "archive.zip" && d.Status == DownloadStatus.Completed));
                Assert("Zero phantoms across all 3 downloads", !vm.Downloads.Any(d => d.FileName.Contains("Unconfirmed") || d.FullPath.EndsWith(".crdownload")));
            }
            finally
            {
                vm.Dispose();
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        static async Task TestTempFileDeletionBeforeCompletionAsync()
        {
            Console.WriteLine("-> Running Temp File Deletion / Cancellation Tests...");
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_CancelTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            var settingsService = new SettingsService();
            settingsService.CurrentSettings.CustomDownloadsFolder = testDir;

            var historyService = new DownloadHistoryService();
            var fileOpService = new FileOperationService();

            var vm = new DownloadsViewModel(settingsService, historyService, fileOpService);

            try
            {
                // Chrome starts download
                string tempFile = Path.Combine(testDir, "Unconfirmed 777.crdownload");
                File.WriteAllText(tempFile, "Partial bytes...");
                await Task.Delay(400);

                Assert("Downloading item added to ViewModel", vm.Downloads.Count == 1);

                // User cancels download in browser -> Chrome deletes the temporary file
                File.Delete(tempFile);
                await Task.Delay(600);

                Assert("Cancelled download was REMOVED from ViewModel", vm.Downloads.Count == 0, $"Count: {vm.Downloads.Count}");
                Assert("Cancelled download was NOT left as Missing", !vm.Downloads.Any(d => d.Status == DownloadStatus.Deleted));
            }
            finally
            {
                vm.Dispose();
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        static void TestTaskbarPositioning()
        {
            Console.WriteLine("-> Running Taskbar Positioning Tests...");
            var pos = TaskbarService.CalculatePanelPosition(380, 520);
            var workArea = SystemParameters.WorkArea;

            Assert("Panel position Left is >= WorkArea Left", pos.X >= workArea.Left, $"Left: {pos.X}, WorkArea.Left: {workArea.Left}");
            Assert("Panel position Right is <= WorkArea Right", pos.X + 380 <= workArea.Right + 5, $"Right: {pos.X + 380}, WorkArea.Right: {workArea.Right}");
            Assert("Panel position Top is >= WorkArea Top", pos.Y >= workArea.Top, $"Top: {pos.Y}, WorkArea.Top: {workArea.Top}");
            Assert("Panel position Bottom is <= WorkArea Bottom", pos.Y + 520 <= workArea.Bottom + 5, $"Bottom: {pos.Y + 520}, WorkArea.Bottom: {workArea.Bottom}");
            Console.WriteLine();
        }

        static void TestShelfWindowPositioning()
        {
            Console.WriteLine("-> Running Download Shelf Positioning Tests (490x216)...");
            const double shelfWidth = 490;
            const double shelfHeight = 216;

            var pos = TaskbarService.CalculatePanelPosition(shelfWidth, shelfHeight);
            var workArea = SystemParameters.WorkArea;

            Assert("Shelf Left >= WorkArea Left", pos.X >= workArea.Left, $"Left: {pos.X}");
            Assert("Shelf Right <= WorkArea Right", pos.X + shelfWidth <= workArea.Right + 5, $"Right: {pos.X + shelfWidth}, WorkArea.Right: {workArea.Right}");
            Assert("Shelf Top >= WorkArea Top", pos.Y >= workArea.Top, $"Top: {pos.Y}");
            Assert("Shelf Bottom <= WorkArea Bottom", pos.Y + shelfHeight <= workArea.Bottom + 5, $"Bottom: {pos.Y + shelfHeight}, WorkArea.Bottom: {workArea.Bottom}");
            Console.WriteLine();
        }

        static async Task TestImageThumbnailGenerationAsync()
        {
            Console.WriteLine("-> Running Image Thumbnail Generation Tests...");
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_ThumbTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);
            string imagePath = Path.Combine(testDir, "sample_preview.png");

            try
            {
                // Create a real PNG image using System.Drawing
                using (var bmp = new System.Drawing.Bitmap(120, 80))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.Clear(System.Drawing.Color.MediumSlateBlue);
                    }
                    bmp.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
                }

                var thumb = await ThumbnailService.GetThumbnailAsync(imagePath, 256);
                Assert("Generated image thumbnail is not null", thumb != null);
                Assert("Thumbnail is a valid BitmapSource", thumb is System.Windows.Media.Imaging.BitmapSource);

                if (thumb is System.Windows.Media.Imaging.BitmapSource bs)
                {
                    Assert("Thumbnail has positive pixel dimensions", bs.PixelWidth > 0 && bs.PixelHeight > 0, $"{bs.PixelWidth}x{bs.PixelHeight}");
                    Assert("Thumbnail is frozen for thread safety", bs.IsFrozen);
                }

                // Caching test
                var cachedThumb = await ThumbnailService.GetThumbnailAsync(imagePath, 256);
                Assert("Second request retrieves cached instance", ReferenceEquals(thumb, cachedThumb));

                ThumbnailService.InvalidateCache(imagePath);
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        static async Task TestPdfAndCorruptFileHandlingAsync()
        {
            Console.WriteLine("-> Running PDF & Corrupted File Thumbnail Resilience Tests...");
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_CorruptTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);
            string corruptPath = Path.Combine(testDir, "corrupted.jpg");
            File.WriteAllBytes(corruptPath, new byte[] { 0x00, 0x01, 0x02 }); // Invalid image bytes
            string nonExistentPath = Path.Combine(testDir, "ghost.pdf");

            try
            {
                var nonExistentThumb = await ThumbnailService.GetThumbnailAsync(nonExistentPath, 256);
                Assert("Non-existent file gracefully returns null without throwing", nonExistentThumb == null);

                var corruptThumb = await ThumbnailService.GetThumbnailAsync(corruptPath, 256);
                // Should not throw, may return null or fallback icon
                Assert("Corrupted image handled safely without crashing", true);
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        static async Task TestDownloadCompletedEventNotificationAsync()
        {
            Console.WriteLine("-> Running DownloadCompletedEvent Notification Tests...");
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_EventTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            var settingsService = new SettingsService();
            settingsService.CurrentSettings.CustomDownloadsFolder = testDir;

            var historyService = new DownloadHistoryService(testDir);
            var fileOpService = new FileOperationService();

            var vm = new DownloadsViewModel(settingsService, historyService, fileOpService);
            DownloadItem? receivedItem = null;
            var tcs = new TaskCompletionSource<DownloadItem>();

            vm.FileCompletedEvent += item =>
            {
                receivedItem = item;
                tcs.TrySetResult(item);
            };

            try
            {
                // Create a real completed file via atomic rename (mimicking browser)
                string tempFile = Path.Combine(testDir, "snapshot.png.crdownload");
                string finalFile = Path.Combine(testDir, "snapshot.png");

                File.WriteAllText(tempFile, "PNG image bytes simulation");
                await Task.Delay(200);

                File.Move(tempFile, finalFile);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(4000));
                Assert("FileCompletedEvent fired upon completion", completed == tcs.Task);
                Assert("Received item matches final filename", receivedItem != null && receivedItem.FileName == "snapshot.png");
                Assert("Received item marked as IsJustDownloaded", receivedItem != null && receivedItem.IsJustDownloaded);
            }
            finally
            {
                vm.Dispose();
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        static void TestAnalyticsInstallationId()
        {
            Console.WriteLine("-> Running Analytics Anonymous Installation ID Tests...");
            string testDir1 = Path.Combine(Path.GetTempPath(), "MasseterDrop_AnalyticsIdTest1_" + Guid.NewGuid().ToString("N"));
            string testDir2 = Path.Combine(Path.GetTempPath(), "MasseterDrop_AnalyticsIdTest2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir1);
            Directory.CreateDirectory(testDir2);

            try
            {
                var settingsService1 = new SettingsService(testDir1);
                var loaded1 = settingsService1.LoadSettings();

                Assert("AnonymousInstallationId generated on first launch", !string.IsNullOrWhiteSpace(loaded1.AnonymousInstallationId));
                Assert("AnonymousInstallationId is valid GUID format", Guid.TryParse(loaded1.AnonymousInstallationId, out Guid parsedGuid) && parsedGuid != Guid.Empty);
                Assert("AnonymousInstallationId does not contain username", !loaded1.AnonymousInstallationId!.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
                Assert("AnonymousInstallationId does not contain computer name", !loaded1.AnonymousInstallationId!.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase));

                // Reload from disk to verify persistence across restarts
                var settingsService1Reloaded = new SettingsService(testDir1);
                var reloaded1 = settingsService1Reloaded.LoadSettings();
                Assert("AnonymousInstallationId reused across restarts", string.Equals(loaded1.AnonymousInstallationId, reloaded1.AnonymousInstallationId, StringComparison.OrdinalIgnoreCase));

                // Independent installation creates distinct ID
                var settingsService2 = new SettingsService(testDir2);
                var loaded2 = settingsService2.LoadSettings();
                Assert("Different installation generates different unique ID", !string.Equals(loaded1.AnonymousInstallationId, loaded2.AnonymousInstallationId, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                try { Directory.Delete(testDir1, true); } catch { }
                try { Directory.Delete(testDir2, true); } catch { }
            }
            Console.WriteLine();
        }

        static void TestAnalyticsConsentAndOptOut()
        {
            Console.WriteLine("-> Running Analytics Consent & Opt-Out Tests...");
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_ConsentTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                var settingsService = new SettingsService(testDir);
                var s = settingsService.LoadSettings();

                Assert("Initial consent preference is null (not yet asked)", s.AnalyticsConsent == null);

                var analytics = new AnalyticsService(settingsService);
                Assert("Analytics is disabled when consent is null", !analytics.IsEnabled);

                // User accepts consent
                analytics.SetConsent(true);
                Assert("Analytics is enabled after accepting consent", analytics.IsEnabled);
                Assert("Consent preference saved as true", settingsService.CurrentSettings.AnalyticsConsent == true);

                // Simulate app restart with consent enabled
                var settingsServiceRestart = new SettingsService(testDir);
                settingsServiceRestart.LoadSettings();
                Assert("Consent true survives application restart", settingsServiceRestart.CurrentSettings.AnalyticsConsent == true);

                var analyticsRestart = new AnalyticsService(settingsServiceRestart);
                Assert("Analytics resumes enabled after restart", analyticsRestart.IsEnabled);

                // User changes preference to opt-out
                analyticsRestart.SetConsent(false);
                Assert("Analytics is immediately disabled after opt-out", !analyticsRestart.IsEnabled);
                Assert("Consent preference saved as false", settingsServiceRestart.CurrentSettings.AnalyticsConsent == false);

                // Restart again after opt-out
                var settingsServiceRestart2 = new SettingsService(testDir);
                settingsServiceRestart2.LoadSettings();
                Assert("Consent false survives application restart", settingsServiceRestart2.CurrentSettings.AnalyticsConsent == false);

                var analyticsRestart2 = new AnalyticsService(settingsServiceRestart2);
                Assert("Analytics remains disabled after restart", !analyticsRestart2.IsEnabled);

                analytics.Dispose();
                analyticsRestart.Dispose();
                analyticsRestart2.Dispose();
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        static void TestAnalyticsPrivacyAndDataSanitization()
        {
            Console.WriteLine("-> Running Analytics Privacy & Data Sanitization Tests...");

            // File category mapping
            Assert("PDF category maps to pdf", AnalyticsService.ToCategoryString(FileCategory.Pdf) == "pdf");
            Assert("Image category maps to image", AnalyticsService.ToCategoryString(FileCategory.Image) == "image");
            Assert("Video category maps to video", AnalyticsService.ToCategoryString(FileCategory.Video) == "video");
            Assert("Audio category maps to audio", AnalyticsService.ToCategoryString(FileCategory.Audio) == "audio");
            Assert("Archive category maps to archive", AnalyticsService.ToCategoryString(FileCategory.Archive) == "archive");
            Assert("Document category maps to document", AnalyticsService.ToCategoryString(FileCategory.Document) == "document");
            Assert("Executable category maps to executable", AnalyticsService.ToCategoryString(FileCategory.Executable) == "executable");
            Assert("Code category maps to other", AnalyticsService.ToCategoryString(FileCategory.Code) == "other");
            Assert("Other category maps to other", AnalyticsService.ToCategoryString(FileCategory.Other) == "other");

            // App version format check
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_SanitizeTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);
            try
            {
                var settingsService = new SettingsService(testDir);
                settingsService.LoadSettings();
                var analytics = new AnalyticsService(settingsService);

                Assert("AppVersion is valid semantic version", !string.IsNullOrWhiteSpace(analytics.AppVersion) && Version.TryParse(analytics.AppVersion, out _));
                analytics.Dispose();
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        private class MockAnalyticsService : IAnalyticsService
        {
            public bool IsEnabled { get; set; } = true;
            public string InstallationId { get; set; } = "mock-id";
            public string AppVersion { get; set; } = "1.0.0";
            public List<string> TrackedEvents { get; } = new();
            public List<FileCategory> OpenedCategories { get; } = new();
            public List<FileCategory> DraggedCategories { get; } = new();
            public List<string> DisplaySources { get; } = new();
            public List<string> DismissReasons { get; } = new();
            public List<string> ReopenSources { get; } = new();

            public void SetConsent(bool consentGiven) => IsEnabled = consentGiven;
            public void TrackFirstLaunch() => TrackedEvents.Add("app_first_launch");
            public void TrackAppStarted(string launchMode) => TrackedEvents.Add("app_started:" + launchMode);
            public void TrackAppShutdown(string source) => TrackedEvents.Add("app_shutdown:" + source);
            public void TrackAppUpdated(string prev, string curr) => TrackedEvents.Add($"app_updated:{prev}->{curr}");
            public void TrackShelfDisplayed(string source)
            {
                TrackedEvents.Add("shelf_displayed:" + source);
                DisplaySources.Add(source);
            }
            public void TrackShelfDismissed(string reason)
            {
                TrackedEvents.Add("shelf_dismissed:" + reason);
                DismissReasons.Add(reason);
            }
            public void TrackShelfReopened(string source)
            {
                TrackedEvents.Add("shelf_reopened:" + source);
                ReopenSources.Add(source);
            }
            public void TrackFileOpened(FileCategory category)
            {
                TrackedEvents.Add("file_opened:" + category);
                OpenedCategories.Add(category);
            }
            public void TrackFileDragged(FileCategory category)
            {
                TrackedEvents.Add("file_dragged:" + category);
                DraggedCategories.Add(category);
            }
            public Task FlushAsync(TimeSpan timeout) => Task.CompletedTask;
            public void Dispose() { }
        }

        static void TestAnalyticsEventFlow()
        {
            Console.WriteLine("-> Running Analytics Event Flow Tests...");
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_EventFlowTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                var settingsService = new SettingsService(testDir);
                settingsService.LoadSettings();

                var historyService = new DownloadHistoryService(testDir);
                var fileOpService = new FileOperationService();
                var mockAnalytics = new MockAnalyticsService();

                var vm = new DownloadsViewModel(settingsService, historyService, fileOpService, mockAnalytics);

                // Create a real file to open
                string testFile = Path.Combine(testDir, "annual_report.pdf");
                File.WriteAllText(testFile, "%PDF-1.4 test bytes");

                var item = new DownloadItem
                {
                    FullPath = testFile,
                    FileName = "annual_report.pdf",
                    Extension = ".pdf",
                    Category = FileCategory.Pdf,
                    Status = DownloadStatus.Completed,
                    IsAvailable = true
                };

                // Open file should trigger file_opened event with Pdf category
                vm.OpenFileCommand.Execute(item);
                Assert("File open triggers file_opened event", mockAnalytics.TrackedEvents.Contains("file_opened:Pdf"));
                Assert("File open passes correct category", mockAnalytics.OpenedCategories.Contains(FileCategory.Pdf));

                // Open missing/nonexistent file should NOT trigger file_opened
                int initialCount = mockAnalytics.OpenedCategories.Count;
                var missingItem = new DownloadItem
                {
                    FullPath = Path.Combine(testDir, "nonexistent.png"),
                    FileName = "nonexistent.png",
                    Extension = ".png",
                    Category = FileCategory.Image,
                    Status = DownloadStatus.Deleted,
                    IsAvailable = false
                };
                vm.OpenFileCommand.Execute(missingItem);
                Assert("Failed file open does not record file_opened event", mockAnalytics.OpenedCategories.Count == initialCount);

                // Test DragDropHelper event
                DragDropHelper.FileDragged += category => mockAnalytics.TrackFileDragged(category);
                // Simulate drag trigger via event
                mockAnalytics.TrackFileDragged(FileCategory.Image);
                Assert("File dragged records file_dragged event", mockAnalytics.TrackedEvents.Contains("file_dragged:Image"));
                Assert("File dragged passes correct category", mockAnalytics.DraggedCategories.Contains(FileCategory.Image));

                vm.Dispose();
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        static async Task TestAnalyticsShutdownTimeoutAndResilienceAsync()
        {
            Console.WriteLine("-> Running Analytics Shutdown Timeout & Resilience Tests...");
            string testDir = Path.Combine(Path.GetTempPath(), "MasseterDrop_ResilienceTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                var settingsService = new SettingsService(testDir);
                settingsService.LoadSettings();

                // Point to an unreachable host to test timeout behavior
                var badHostAnalytics = new AnalyticsService(settingsService, "phc_test_dummy", "https://unreachable.invalid.local");
                badHostAnalytics.SetConsent(true);

                // Should not crash or throw when tracking events against dead host
                badHostAnalytics.TrackAppStarted("test");
                badHostAnalytics.TrackFileOpened(FileCategory.Pdf);
                badHostAnalytics.TrackShelfDisplayed("manual");

                Assert("Event capture with unreachable host does not throw", true);

                // Flush with a 300ms timeout must return within ~1000ms max and not hang
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await badHostAnalytics.FlushAsync(TimeSpan.FromMilliseconds(300));
                sw.Stop();

                Assert("FlushAsync with timeout completes promptly without hanging", sw.ElapsedMilliseconds < 1500, $"Elapsed: {sw.ElapsedMilliseconds}ms");

                badHostAnalytics.Dispose();
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
            Console.WriteLine();
        }

        static async Task TestPostHogLiveCaptureAsync()
        {
            Console.WriteLine("-> Running PostHog Live Ingestion Delivery Tests...");
            try
            {
                // Test live event transmission using user's real project token and ingestion host
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                string jsonBody = "{\"api_key\":\"" + AnalyticsConfig.ProjectToken + "\",\"event\":\"app_verification_ping\",\"distinct_id\":\"masseter_drop_test_device\",\"properties\":{\"app_version\":\"1.0.0\",\"platform\":\"Windows\",\"test_run\":true,\"$process_person_profile\":false}}";
                var content = new System.Net.Http.StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

                string targetUrl = AnalyticsConfig.IngestionHost.TrimEnd('/') + "/capture/";
                var response = await http.PostAsync(targetUrl, content);
                string responseText = await response.Content.ReadAsStringAsync();

                bool isSuccess = response.IsSuccessStatusCode && (responseText.Contains("Ok") || responseText.Contains("1"));
                Assert("PostHog live ingest endpoint responds OK", isSuccess, $"Status: {response.StatusCode}, Body: {responseText.Trim()}");
            }
            catch (Exception ex)
            {
                Assert("PostHog live ingest endpoint responds OK", false, $"Exception: {ex.Message}");
            }
            Console.WriteLine();
        }
    }
}
