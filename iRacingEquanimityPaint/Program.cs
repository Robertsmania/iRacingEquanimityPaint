/*
 * Copyright 2024 Robertsmania
 * All Rights Reserved
 */
using HerboldRacing; //IRSDKSharper
using System.Text.Json;
using System.Reflection;

namespace iRacingEquanimityPaint
{
    class Program
    {
        const int cTextureUpdateTimer = 500;

        static string documentsFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        static bool iRacingConnected = false;
        static bool useSpecMap = true;
        static string logFileName = "";
        static Random random = new Random();
        static Options userOptions = new Options();

        static SemaphoreSlim sessionUpdateSemaphore = new SemaphoreSlim(1, 1);
        static SemaphoreSlim textureUpdateSemaphore = new SemaphoreSlim(1, 1);

        static int subSessionID = 0;
        static int driverCarIdx = 0;
        static Dictionary<int, IRacingSdkSessionInfo.DriverInfoModel.DriverModel> driverCache = new Dictionary<int, IRacingSdkSessionInfo.DriverInfoModel.DriverModel>();
        static IRSDKSharper irsdk = new IRSDKSharper();

        static string startArg = "";

        static async Task Main(string[] args)
        {
            startArg = args?.FirstOrDefault() ?? "";

            if (startArg == "iRacingPaints")
            {
                Console.WriteLine("iRacingPaints command line option - deleting paints and reloading textures.");
                userOptions.DeletePaintsFolder = true;
                CleanUp();
                irsdk.ReloadTextures(IRacingSdkEnum.ReloadTexturesMode.All, 0);
                return;
            }

            const string appName = "iRacingEquanimityPaint";
            bool createdNew;

            using (Mutex mutex = new Mutex(initiallyOwned: true, name: appName, out createdNew))
            {
                if (!createdNew)
                {
                    Console.WriteLine($"Only one instance of {appName} can be running at a time.");
                    return;
                }

                Console.WriteLine("\nRobertsmania iRacingEquanimityPaint started. Press Q to quit. R to force a re-run.");
                Console.WriteLine("Use Ctrl-R in game to force the paints to update.\n");

                SetupLogging();
                userOptions = LoadOptions();

                if (userOptions.RandomMode)
                {
                    SetupRandomFiles();
                }

                CleanUp();
                var cancellationTokenSource = new CancellationTokenSource();

                Console.CancelKeyPress += (sender, eventArgs) =>
                {
                    if (!userOptions.QuitAfterCopy)
                    {
                        Console.WriteLine("\nCtrl+C detected. Cleaning up...");
                        CleanUp();
                    }
                };

                AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) =>
                {
                    if (!userOptions.QuitAfterCopy)
                    {
                        Console.WriteLine("\nProcess exit detected. Cleaning up...");
                        CleanUp();
                    }
                };

                Console.WriteLine("\nWaiting for iRacing.");
                irsdk.OnSessionInfo += OnSessionInfo;
                irsdk.OnConnected += OnConnected;
                irsdk.OnDisconnected += OnDisconnected;

                irsdk.Start();

                // Start an asynchronous task to monitor for quit key press
                var quitTask = MonitorUserInputAsync(cancellationTokenSource.Token);
                try
                {
                    // Wait until the quitTask completes, i.e., when Q is pressed
                    await quitTask;
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Exiting gracefully...");
                    if (!userOptions.QuitAfterCopy)
                    {
                        CleanUp();
                    }
                }
                finally
                {
                    irsdk.Stop();
                    cancellationTokenSource.Cancel();
                    cancellationTokenSource.Dispose();
                }
            }
        }

        static void OnConnected()
        {
            iRacingConnected = true;
            Console.WriteLine("\nConnected to iRacing");

            if (!userOptions.RandomMode)
            {
                UseRandomSpecMap();
            }
        }

        static void OnDisconnected()
        {
            iRacingConnected = false;
            Console.WriteLine("\nDisconnected from iRacing");

            CleanUp();
        }

        static async void OnSessionInfo()
        {
            if (userOptions.OnlyRaces && irsdk.Data.SessionInfo.WeekendInfo.EventType != "Race") 
            { 
                return; 
            }

            //var startTime = DateTime.Now;
            //Console.WriteLine($"Session data updated - {startTime}");
            await sessionUpdateSemaphore.WaitAsync();
            try
            {
                var thisSubSessionID = irsdk.Data.SessionInfo.WeekendInfo.SubSessionID;
                var driverInfo = irsdk.Data.SessionInfo.DriverInfo.Drivers;
                driverCarIdx = irsdk.Data.SessionInfo.DriverInfo.DriverCarIdx;
                if (subSessionID != thisSubSessionID)
                {
                    subSessionID = thisSubSessionID;
                    Console.WriteLine($"\nNew Session! {thisSubSessionID}");
                    driverCache.Clear();
                }
                //Console.WriteLine($"Updating driver cache - {startTime}");
                UpdateDriverCache(driverInfo);
            }
            catch (Exception ex) 
            {
                Log($"Error handling session update: {ex.Message}");
            }
            finally
            {
                sessionUpdateSemaphore.Release();
            }
        }

        static void UpdateDriverCache(List<IRacingSdkSessionInfo.DriverInfoModel.DriverModel> currentDriverModels)
        {
            bool driversUpdated = false;
            foreach (var driverModel in currentDriverModels)
            {
                //Don't add "us" or the pace car
                if (driverModel.UserID < 1 || driverModel.CarIdx == driverCarIdx)
                {
                    continue;
                }

                //New here?
                if (!driverCache.ContainsKey(driverModel.UserID))
                {
                    driversUpdated = true;
                    driverCache.Add(driverModel.UserID, driverModel);
                    Console.WriteLine($"Added new driver #{driverModel.CarNumber,2} with car path: {driverModel.CarPath} UserID: {driverModel.UserID}");

                    if (userOptions.RandomMode)
                    {
                        if (startArg == "RandomPerDriver")
                        {
                            SetupRandomFiles();
                        }
                        CopyPaint(driverModel.UserID, driverModel.CarPath, "car_common.tga", "random_selection");
                        CopyPaint(driverModel.UserID, driverModel.CarPath, "car_num_common.tga", "random_selection");
                        CopyPaint(driverModel.UserID, driverModel.CarPath, "car_spec_common.mip", "random_selection");
                        CopyPaint(driverModel.UserID, driverModel.CarPath, "helmet_common.tga", "random_selection");
                        CopyPaint(driverModel.UserID, driverModel.CarPath, "suit_common.tga", "random_selection");
                    }
                    else
                    {
                        if (useSpecMap)
                        {
                            CopyPaint(driverModel.UserID, driverModel.CarPath, "car_spec_common.mip");
                        }
                        else
                        {
                            DeletePaint(driverModel.UserID, driverModel.CarPath, "car_spec_common.mip");
                        }
                        CopyPaint(driverModel.UserID, driverModel.CarPath, "car_common.tga");
                        CopyPaint(driverModel.UserID, driverModel.CarPath, "car_num_common.tga");
                        CopyPaint(driverModel.UserID, driverModel.CarPath, "car_decal_common.tga");
                        CopyPaint(driverModel.UserID, driverModel.CarPath, "helmet_common.tga", !userOptions.CarSpecificHelmetSuit ? "helmet" : null);
                        CopyPaint(driverModel.UserID, driverModel.CarPath, "suit_common.tga", !userOptions.CarSpecificHelmetSuit ? "suit" : null);
                    }
                    //Request texture reload, but dont wait - they will qeueu up...
                    RequestTextureReload(driverModel.CarIdx).ConfigureAwait(false);
                }
            }
            if (driversUpdated)
            {
                Console.Write("Requesting iRacing texture reload for each car: ");
                //This final one will exit the program if QuitAfterCopy is true.
                RequestTextureReload(-1, userOptions.QuitAfterCopy).ConfigureAwait(false);
            }
        }

        static async Task RequestTextureReload(int carIdx, bool quit = false)
        {
            // Wait to enter the semaphore
            await textureUpdateSemaphore.WaitAsync();
            if (userOptions.QuitAfterCopy && quit && carIdx == -1) //really sure?
            {
                Console.WriteLine("\nAll texture updates requested. QuitAfterCopy is true, exiting application...");
                Environment.Exit(0);
            }
            
            if (carIdx == -1)
            {
                Console.WriteLine("\nAll texture updates requested.");
                // Release the semaphore to allow the next queued task to proceed
                textureUpdateSemaphore.Release();
                return;
            }

            //Or actually request the reload...
            try
            {
                // Delay before executing the reload to prevent spamming
                await Task.Delay(cTextureUpdateTimer);
                Console.Write("*");

                // Call the iRacing SDK to reload textures for the specific car
                irsdk.ReloadTextures(IRacingSdkEnum.ReloadTexturesMode.CarIdx, carIdx);
                //Console.WriteLine($"Requested texture reload for car index: {carIdx}");
            }
            catch (Exception ex)
            {
                // Handle exceptions that might occur during the reload request
                Log($"Error during texture reload for car index {carIdx}: {ex.Message}");
            }
            finally
            {
                // Release the semaphore to allow the next queued task to proceed
                textureUpdateSemaphore.Release();
            }
        }

        static void CopyPaint(int userID, string carPath, string commonFileName, string? commonPath = null)
        {
            string userFileName = commonFileName.Replace("common", userID.ToString());
            string commonFilePath = Path.Combine(documentsFolderPath, "iRacing", "paintcommon", commonPath ?? carPath, commonFileName);
            string userFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, userFileName);
            string userFolderPath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath);

            try
            {
                if (File.Exists(commonFilePath))
                {
                    // Ceate the userFolderPath if necessary
                    if (!Directory.Exists(userFolderPath))
                    {
                        Directory.CreateDirectory(userFolderPath);
                        Console.WriteLine($"Created directory: {userFolderPath}");
                    }

                    // Check if the destination file exists and is read-only
                    if (File.Exists(userFilePath))
                    {
                        // Remove read-only attribute to overwrite
                        FileAttributes attributes = File.GetAttributes(userFilePath);
                        if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        {
                            File.SetAttributes(userFilePath, attributes & ~FileAttributes.ReadOnly);
                            //Console.WriteLine($"Removed read-only attribute from: {userFilePath}");
                        }
                    }

                    File.Copy(commonFilePath, userFilePath, true);
                    //Console.WriteLine($"Copied common file to: {userFilePath}");
                    File.SetLastWriteTime(userFilePath, DateTime.Now);

                }
                else
                {
                    Log($"Common file does not exist: {commonFilePath}");
                }
            }
            catch (UnauthorizedAccessException e)
            {
                Log($"Access denied. Cannot write to the paint file: {e.Message}");
            }
            catch (PathTooLongException e)
            {
                Log($"The specified path is too long: {e.Message}");
            }
            catch (DirectoryNotFoundException e)
            {
                Log($"The specified directory was not found: {e.Message}");
            }
            catch (IOException e)
            {
                Log($"An I/O error occurred while copying the file: {e.Message}");
            }
            catch (Exception e)
            {
                Log($"An unexpected error occurred: {e.Message}");
            }
        }

        static void DeletePaint(int userID, string carPath, string commonFileName)
        {
            string userFileName = commonFileName.Replace("common", userID.ToString());
            string userFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, userFileName);

            try
            {
                if (File.Exists(userFilePath))
                {
                    // Remove read-only attribute if it's set
                    FileAttributes attributes = File.GetAttributes(userFilePath);
                    if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(userFilePath, attributes & ~FileAttributes.ReadOnly);
                        //Console.WriteLine($"Removed read-only attribute from: {userFilePath}");
                    }

                    // Delete the file
                    File.Delete(userFilePath);
                    //Console.WriteLine($"Deleted file: {userFilePath}");
                }
                else
                {
                    //Console.WriteLine($"File does not exist and cannot be deleted: {userFilePath}");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Log($"Access denied. Cannot delete the paint file: {ex.Message}");
            }
            catch (IOException ex)
            {
                Log($"An I/O error occurred while deleting the file: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log($"An unexpected error occurred: {ex.Message}");
            }
        }

        static void SetupRandomFiles()
        {
            CleanUpRandomSelection();
            Console.WriteLine("\nSetting up random files.");
            string sourceDir = Path.Combine(documentsFolderPath, "iRacing", "paintcommon", "random_source");
            string targetDir = Path.Combine(documentsFolderPath, "iRacing", "paintcommon", "random_selection");
            Directory.CreateDirectory(targetDir);  

            Dictionary<string, string> categories = new Dictionary<string, string>
            {
                { "car", "car_common.tga" },
                { "car_num", startArg == "RandomPerDriver" ? "car_common.tga" : "car_num_common.tga" },
                { "car_spec", "car_spec_common.mip" },
                { "helmet", "helmet_common.tga" },
                { "suit", "suit_common.tga" }
            };

            Random rand = new Random();

            foreach (var category in categories)
            {
                try
                {
                    string sourceFolder = Path.Combine(sourceDir, category.Key);
                    string[] files = Directory.GetFiles(sourceFolder);

                    if (files.Length == 0)
                    {
                        Log($"No files found in {sourceFolder}. Skipping.");
                        continue;
                    }

                    // Select a random file
                    string selectedFile = files[rand.Next(files.Length)];
                    string targetFile = Path.Combine(targetDir, category.Value);
                    // Copy the file to the new location with the new name
                    File.Copy(selectedFile, targetFile, true);
                    Console.WriteLine($"Copied: {selectedFile} -> {category.Value}.");
                }
                catch (Exception ex)
                {
                    Log($"Error trying to copy: {category.Value} - {ex.Message}");
                }
            }
        }

        static void UseRandomSpecMap()
        {
            int chance = random.Next(100);
            // Determine whether to use the spec map based on SpecMapPercentageChance.
            useSpecMap = chance < userOptions.SpecMapPercentageChance;
            Console.WriteLine($"\nUse Spec Map: {useSpecMap}");
        }

        static void CleanUp()
        {
            if (userOptions.DeletePaintsFolder)
            {
                try
                {
                    subSessionID = 0;
                    string paintFolder = Path.Combine(documentsFolderPath, "iRacing", "paint");
                    RemoveReadOnlyAttributes(paintFolder);
                    Directory.Delete(paintFolder, true);
                    //Console.WriteLine("\nDeleted paints folder");
                }
                catch (DirectoryNotFoundException)
                {
                    //Console.WriteLine("\nNo paints folder.");
                }
                catch (Exception ex)
                {
                    Log($"Could not clean up paints folder: {ex.Message}");
                }
            }
        }

        static void CleanUpRandomSelection()
        {
            if (userOptions.RandomMode)
            {
                try
                {
                    string randomSelectionFolder = Path.Combine(documentsFolderPath, "iRacing", "paintcommon", "random_selection");
                    RemoveReadOnlyAttributes(randomSelectionFolder);
                    Directory.Delete(randomSelectionFolder, true);
                    //Console.WriteLine("\nDeleted random_selected paints folder");
                }
                catch (DirectoryNotFoundException)
                {
                    //Console.WriteLine("\nNo random_selected paints folder.");
                }
                catch (Exception ex)
                {
                    Log($"Could not clean up random_selected paints folder: {ex.Message}");
                }
            }
        }

        static void RemoveReadOnlyAttributes(string directoryPath)
        {
            // Check if the directory exists
            if (!Directory.Exists(directoryPath))
                return;

            // Remove read-only attribute from all files in the directory
            var fileEntries = Directory.GetFiles(directoryPath);
            foreach (var file in fileEntries)
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            // Recurse into subdirectories
            var subdirectoryEntries = Directory.GetDirectories(directoryPath);
            foreach (var subdirectory in subdirectoryEntries)
                RemoveReadOnlyAttributes(subdirectory);
        }

        public static Options LoadOptions()
        {
            string fileName = "Options.json";
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            Options loadOptions = new Options();

            if (File.Exists(fileName))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    loadOptions = JsonSerializer.Deserialize<Options>(json);
                    SaveOptions(loadOptions, configPath);
                }
                catch (Exception ex)
                {
                    Log($"Error loading {fileName} configuration. Using defaults. Error: {ex.Message}");
                    loadOptions = new Options(); 
                    SaveOptions(loadOptions, configPath);
                }
            }
            else
            {
                Log($"No {fileName} file found. Using defaults.");
                loadOptions = new Options();
                SaveOptions(loadOptions, configPath);
            }

            Console.WriteLine($"Options:");
            foreach (PropertyInfo property in loadOptions.GetType().GetProperties())
            {
                // Get the value of the property
                var value = property.GetValue(loadOptions, null);
                Console.WriteLine($"{property.Name}: {value}");
            }

            if (loadOptions.RandomMode)
            {
                Console.WriteLine("RandomMode is enabled, so CarSpecificHelmetSuit and SpecMapPercentageChance are ignored.");
            }
            return loadOptions;
        }

        public static void SaveOptions(Options options, string configPath)
        {
            try
            {
                string json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
                //Console.WriteLine($"Default configuration saved to {configPath}");
            }
            catch (Exception ex)
            {
                Log("Failed to save default configuration. Error: " + ex.Message);
            }
        }

        // Asynchronously wait for the user to press 'Q' to quit or 'R' to re-run
        static async Task MonitorUserInputAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Q)
                    {
                        break;  
                    }
                    else if (key == ConsoleKey.R) 
                    {
                        Console.WriteLine("\n'R' key pressed.");
                        userOptions = LoadOptions();
                        if (userOptions.RandomMode)
                        {
                            SetupRandomFiles();
                        }
                        else
                        {
                            UseRandomSpecMap();
                        }
                        if (iRacingConnected)
                        {
                            Console.WriteLine("\nForcing a re-run.");
                            subSessionID = 0;
                            OnSessionInfo();
                        }
                        else
                        {
                            Console.WriteLine("\nWaiting for iRacing.");
                        }
                    }
                }
                await Task.Delay(100, cancellationToken); 
            }
        }

        static void SetupLogging()
        {
            string logDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectoryPath);

            string dateTimeStamp = DateTime.Now.ToString("yyMMdd_HHmmss");
            logFileName = Path.Combine(logDirectoryPath, $"irEP_{dateTimeStamp}.txt");

            //Log("irEP Logging started.");
        }

        static void Log(string message)
        {
            Console.WriteLine(message);

            if (!userOptions.LogToFile || string.IsNullOrEmpty(logFileName))
            {
                return;
            }
            try
            {
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}\n";
                File.AppendAllText(logFileName, logEntry);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }

        public struct Options
        {
            public bool RandomMode { get; set; } = true;
            public bool QuitAfterCopy { get; set; } = false;
            public bool DeletePaintsFolder { set; get; } = false;
            public bool OnlyRaces { get; set; } = false;
            public int SpecMapPercentageChance { set; get; } = 100;
            public bool CarSpecificHelmetSuit { set; get; } = false;
            public bool LogToFile { get; set; } = true;
            public Options()
            {
            }
        }
    }
}
