/*
 * Copyright 2024 Robertsmania
 * All Rights Reserved
 */
using HerboldRacing; //IRSDKSharper
using System.Text.Json;
using System.IO;
using static iRacingEquanimityPaint.Program;
using System.Reflection;

namespace iRacingEquanimityPaint
{
    class Program
    {
        const int cLoadTimer = 2000;
        const int cUpdateTimer = 100;

        static Options userOptions = new Options();
        static bool iRacingConnected = false;
        static SemaphoreSlim updateSemaphore = new SemaphoreSlim(1, 1);

        static IRSDKSharper irsdk = new IRSDKSharper();
        static int subSessionID = 0;
        static int driverCarIdx = 0;
        static Dictionary<int, IRacingSdkSessionInfo.DriverInfoModel.DriverModel> driverCache = new Dictionary<int, IRacingSdkSessionInfo.DriverInfoModel.DriverModel>();

        static string documentsFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        static Random random = new Random();
        static bool useSpecMap = true;

        static async Task Main(string[] args)
        {
            Console.WriteLine("Robertsmania iRacingEquanimityPaint started. Press Q to quit. R to force a re-run.");
            Console.WriteLine("Use Ctrl-R in game to force the paints to update.\n"); 

            userOptions = LoadOptions();
            CleanUp();
            var cancellationTokenSource = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                Console.WriteLine("Ctrl+C detected. Cleaning up...");
                CleanUp();
            };

            AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) =>
            {
                Console.WriteLine("Process exit detected. Cleaning up...");
                CleanUp();
            };

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
                CleanUp();
            }
            finally
            {
                irsdk.Stop();  
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
            }
        }

        static void OnConnected()
        {
            iRacingConnected = true;
            Console.WriteLine("\nConnected to iRacing");

            UseRandomSpecMap();
        }

        static void OnDisconnected()
        {
            iRacingConnected = false;
            Console.WriteLine("\nDisconnected from iRacing");

            CleanUp();
        }

        static void CleanUp()
        {
            subSessionID = 0;
            string paintFolder = Path.Combine(documentsFolderPath, "iRacing", "paint");
            try
            {
                RemoveReadOnlyAttributes(paintFolder);
                if (userOptions.DeletePaintsFolder)
                {
                    Directory.Delete(paintFolder, true);
                    Console.WriteLine("Deleted paints folder");
                }
            }
            catch (DirectoryNotFoundException e)
            {
                Console.WriteLine("No paints folder.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not clean up paints folder: {ex.Message}");
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

        static async void OnSessionInfo()
        {
            if (userOptions.OnlyRaces && irsdk.Data.SessionInfo.WeekendInfo.EventType != "Race") 
            { 
                return; 
            }

            //var startTime = DateTime.Now;
            //Console.WriteLine($"Session data updated - {startTime}");
            await updateSemaphore.WaitAsync();
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
                    await Task.Delay(cLoadTimer); //Delay to let iRacing load completely
                }
                //Console.WriteLine($"Updating driver cache - {startTime}");
                await UpdateDriverCache(driverInfo);
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"Error handing session update: {ex.Message}");
            }
            finally
            {
                updateSemaphore.Release();
            }
        }

        static async Task UpdateDriverCache(List<IRacingSdkSessionInfo.DriverInfoModel.DriverModel> currentDriverModels)
        {
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
                    driverCache.Add(driverModel.UserID, driverModel);
                    Console.WriteLine($"Added new driver #{driverModel.CarNumber,2} with car path: {driverModel.CarPath} UserID: {driverModel.UserID}");
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
                    await Task.Delay(cUpdateTimer); //Delay to avoid texture reload spamming  
                    irsdk.ReloadTextures(IRacingSdkEnum.ReloadTexturesMode.CarIdx, driverModel.CarIdx);
                }
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

                    // Set the copied file to read-only
                    File.SetAttributes(userFilePath, File.GetAttributes(userFilePath) | FileAttributes.ReadOnly);
                    //Console.WriteLine($"Set file as read-only: {userFilePath}");
                }
                else
                {
                    Console.WriteLine($"Common file does not exist: {commonFilePath}");
                }
            }
            catch (UnauthorizedAccessException e)
            {
                Console.WriteLine($"Access denied. Cannot write to the paint file: {e.Message}");
            }
            catch (PathTooLongException e)
            {
                Console.WriteLine($"The specified path is too long: {e.Message}");
            }
            catch (DirectoryNotFoundException e)
            {
                Console.WriteLine($"The specified directory was not found: {e.Message}");
            }
            catch (IOException e)
            {
                Console.WriteLine($"An I/O error occurred while copying the file: {e.Message}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"An unexpected error occurred: {e.Message}");
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
                Console.WriteLine($"Access denied. Cannot delete the paint file: {ex.Message}");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"An I/O error occurred while deleting the file: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
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
                        if (iRacingConnected)
                        {
                            Console.WriteLine("\nForcing a re-run.");
                            userOptions = LoadOptions();
                            CleanUp();
                            UseRandomSpecMap();
                            subSessionID = 0;
                            OnSessionInfo();
                        }
                        else
                        {
                            Console.WriteLine("Not connected to iRacing.");
                        }
                    }
                }
                await Task.Delay(100, cancellationToken); 
            }
        }

        static void UseRandomSpecMap()
        {
            int chance = random.Next(100);
            // Determine whether to use the spec map based on SpecMapPercentageChance.
            useSpecMap = chance < userOptions.SpecMapPercentageChance;
            Console.WriteLine($"\nUse Spec Map: {useSpecMap}");
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
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading {fileName} configuration. Using defaults. Error: {ex.Message}");
                    loadOptions = new Options(); 
                    SaveOptions(loadOptions, configPath);
                }
            }
            else
            {
                Console.WriteLine($"No {fileName} file found. Using defaults.");
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
            return loadOptions;
        }

        public static void SaveOptions(Options options, string configPath)
        {
            try
            {
                string json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
                Console.WriteLine($"Default configuration saved to {configPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to save default configuration. Error: " + ex.Message);
            }
        }


        public struct Options
        {
            public int SpecMapPercentageChance { set; get; } = 100;
            public bool DeletePaintsFolder { set; get; } = false;
            public bool CarSpecificHelmetSuit { set; get; } = false;
            public bool OnlyRaces { get; set; } = true;
            public Options()
            {
            }
        }
    }
}
