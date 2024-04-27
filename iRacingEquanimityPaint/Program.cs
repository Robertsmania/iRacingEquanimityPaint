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

            Console.WriteLine("Application started. Press Q to quit. R to force a re-run.");
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
                //Delay to let iRacing load completely
                await Task.Delay(cLoadTimer);
            }

            UpdateDriverCache(driverInfo);
        }

        static void UpdateDriverCache(List<IRacingSdkSessionInfo.DriverInfoModel.DriverModel> currentDriverModels)
        {
            // Add new drivers to the cache
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
                    CopyPaint(driverModel.UserID, driverModel.CarPath, "_");
                    CopyPaint(driverModel.UserID, driverModel.CarPath, "_num_");
                    CopyPaint(driverModel.UserID, driverModel.CarPath, "_decal_");
                    CopyHelmet(driverModel.UserID);
                    irsdk.ReloadTextures(IRacingSdkEnum.ReloadTexturesMode.CarIdx, driverModel.CarIdx);
                }
            }
        }

        static void CopyPaint(int userID, string carPath, string paintType)
        {
            try
            {
                // Construct the path to the common paint file
                string commonPaint = $"car{paintType}common.tga";
                string commonPaintFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, "common", commonPaint);

                // Check if the common paint file exists
                if (File.Exists(commonPaintFilePath))
                {
                    // Construct the path to the user-specific paint file
                    string userPaint = $"car{paintType}{userID}.tga";
                    string userPaintFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, userPaint);

                    // Copy the common paint file to the user-specific paint file, overwriting if it already exists
                    File.Copy(commonPaintFilePath, userPaintFilePath, true);
                    Console.WriteLine($"Copied common paint to: {userPaintFilePath}");
                }
                else
                {
                    Console.WriteLine($"Common paint file does not exist: {commonPaintFilePath}");
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

        static void CopyHelmet(int userID)
        {
            try
            {
                // Common helmet
                const string commonHelmet = "helmet_common.tga";
                string commonHelmetFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", "helmets", "common", commonHelmet);

                // Check if the common helmet file exists
                if (File.Exists(commonHelmetFilePath))
                {
                    // Construct the path to the user-specific helmet file
                    string userHelmet = $"{userID}.tga";
                    string userHelmetFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", "helmets", userHelmet);

                    // Copy the common helmet file to the user-specific helmet file, overwriting if it already exists
                    File.Copy(commonHelmetFilePath, userHelmetFilePath, true);
                    Console.WriteLine($"Copied common helmet to: {userHelmetFilePath}");
                }
                else
                {
                    Console.WriteLine($"Common helmet file does not exist: {commonHelmetFilePath}");
                }
            }
            catch (UnauthorizedAccessException e)
            {
                Console.WriteLine($"Access denied. Cannot write to the helmet file: {e.Message}");
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
                        break;  // Exit the loop, leading to a program stop.
                    }
                    else if (key == ConsoleKey.R) 
                    {
                        subSessionID = 0;
                        OnSessionInfo();
                    }
                }
                await Task.Delay(100, cancellationToken); // wait before checking again
            }
        }
    }
}
