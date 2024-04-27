/*
 * Copyright 2024 Robertsmania
 * All Rights Reserved
 */
using HerboldRacing; //IRSDKSharper

namespace iRacingEquanimityPaint
{
    class Program
    {
        const int cLoadTimer = 10000;
        static IRSDKSharper irsdk = new IRSDKSharper();
        static int subSessionID = 0;
        static int driverCarIdx = 0;
        static Dictionary<int, IRacingSdkSessionInfo.DriverInfoModel.DriverModel> driverCache = new Dictionary<int, IRacingSdkSessionInfo.DriverInfoModel.DriverModel>();
        static string documentsFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        static async Task Main(string[] args)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            irsdk.OnSessionInfo += OnSessionInfo;  

            irsdk.Start();  

            // Start an asynchronous task to monitor for quit key press
            var quitTask = MonitorUserInputAsync(cancellationTokenSource.Token);

            Console.WriteLine("iRacingEquanimityPaint started. Press Q to quit. R to force a re-run.");
            Console.WriteLine("Use Ctrl-R in game to force the paints to update."); 

            try
            {
                // Wait until the quitTask completes, i.e., when Q is pressed
                await quitTask;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Exiting gracefully...");
            }
            finally
            {
                irsdk.Stop();  
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
            }
        }

        static async void OnSessionInfo()
        {
            var thisSubSessionID = irsdk.Data.SessionInfo.WeekendInfo.SubSessionID;
            var driverInfo = irsdk.Data.SessionInfo.DriverInfo.Drivers;
            driverCarIdx = irsdk.Data.SessionInfo.DriverInfo.DriverCarIdx;

            if (subSessionID != thisSubSessionID)
            {
                subSessionID = thisSubSessionID;
                Console.WriteLine($"New Session! {thisSubSessionID}");
                driverCache.Clear();
                await Task.Delay(cLoadTimer); //Delay to let iRacing load completely
            }

            UpdateDriverCache(driverInfo);
        }

        // Add new drivers to the cache
        static void UpdateDriverCache(List<IRacingSdkSessionInfo.DriverInfoModel.DriverModel> currentDriverModels)
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
                    Console.WriteLine($"Added new driver {driverModel.UserID} to cache with car path: {driverModel.CarPath}");
                    CopyCarPaints(driverModel.UserID, driverModel.CarPath);
                    CopyDriverPaints(driverModel.UserID);
                    irsdk.ReloadTextures(IRacingSdkEnum.ReloadTexturesMode.CarIdx, driverModel.CarIdx);
                }
            }
        }

        static void CopyCarPaints(int userID, string carPath)
        {
            string commonCarPaintFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, "common", "car_common.tga");
            string commonNumPaintFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, "common", "car_num_common.tga");
            string commonDecalPaintFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, "common", "car_decal_common.tga");

            string userCarPaintFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, $"car_{userID}.tga");
            string userNumPaintFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, $"car_num_{userID}.tga");
            string userDecalPaintFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, $"car_decal_{userID}.tga");
            
            CopyTexture(commonCarPaintFilePath, userCarPaintFilePath);
            CopyTexture(commonNumPaintFilePath, userNumPaintFilePath);
            CopyTexture(commonDecalPaintFilePath, userDecalPaintFilePath);
        }

        static void CopyDriverPaints(int userID)
        {
            string commonHelmetFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", "helmets", "common", "helmet_common.tga");
            string commonSuitFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", "suits", "common", "suit_common.tga");

            string userHelmetFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", "helmets", $"{userID}.tga");
            string userSuitFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", "suits", $"{userID}.tga");

            CopyTexture(commonHelmetFilePath, userHelmetFilePath);
            CopyTexture(commonSuitFilePath, userSuitFilePath);
        }

        static void CopyTexture(string commonFilePath, string userFilePath)
        {
            try
            {
                if (File.Exists(commonFilePath))
                {
                    File.Copy(commonFilePath, userFilePath, true);
                    Console.WriteLine($"Copied common file to: {userFilePath}");
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
                        subSessionID = 0;
                        OnSessionInfo();
                    }
                }
                await Task.Delay(100, cancellationToken); 
            }
        }
    }
}
