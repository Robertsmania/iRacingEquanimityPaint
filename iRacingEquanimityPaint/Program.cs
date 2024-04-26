/*
 * Copyright 2024 Robertsmania
 * All Rights Reserved
 */
using HerboldRacing; // Assume IRSDKSharper and relevant classes are in this namespace

namespace iRacingEquanimityPaint
{
    class Program
    {
        static IRSDKSharper irsdk = new IRSDKSharper();
        static int subSessionID = 0;
        static int driverCarIdx = 0;
        // Cache for storing driver information using UserID as the key
        static Dictionary<int, IRacingSdkSessionInfo.DriverInfoModel.DriverModel> driverCache = new Dictionary<int, IRacingSdkSessionInfo.DriverInfoModel.DriverModel>();
        static string documentsFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        static async Task Main(string[] args)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            irsdk.OnSessionInfo += OnSessionInfo;  // Hook the event handler for session info.

            irsdk.Start();  // Start listening to iRacing data.

            // Start an asynchronous task to monitor for quit key press
            var quitTask = MonitorQuitAsync(cancellationTokenSource.Token);

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
                irsdk.Stop();  // Ensure the SDK is properly stopped on exit.
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
            }
        }

        static void OnSessionInfo()
        {
            var thisSubSessionID = irsdk.Data.SessionInfo.WeekendInfo.SubSessionID;
            var trackName = irsdk.Data.SessionInfo.WeekendInfo.TrackName;
            var driverInfo = irsdk.Data.SessionInfo.DriverInfo.Drivers;
            var driverCarIdx = irsdk.Data.SessionInfo.DriverInfo.DriverCarIdx;

            if (subSessionID != thisSubSessionID)
            {
                subSessionID = thisSubSessionID;
                Console.WriteLine($"New Session! {thisSubSessionID}");
                driverCache.Clear();
            }

            UpdateDriverCache(driverInfo);
        }

        static void UpdateDriverCache(List<IRacingSdkSessionInfo.DriverInfoModel.DriverModel> currentDriverModels)
        {
            // Add new drivers to the cache
            foreach (var driverModel in currentDriverModels)
            {
                //Don't add "us" or the pace car
                if (driverModel.UserID < 1 || driverModel.UserID == driverCarIdx)
                {
                    continue;
                }

                if (!driverCache.ContainsKey(driverModel.UserID))
                {
                    driverCache.Add(driverModel.UserID, driverModel);
                    Console.WriteLine($"Added new driver {driverModel.UserID} to cache with car path: {driverModel.CarPath}");
                    // Here you can call a method to handle the car livery update for the new driver
                    CopyPaint(driverModel.UserID, driverModel.CarPath);
                    irsdk.ReloadTextures(IRacingSdkEnum.ReloadTexturesMode.CarIdx, driverModel.CarIdx);
                }
            }
        }

        static void CopyPaint(int userID, string carPath)
        {
            try
            {
                // Construct the path to the common paint file
                string commonPaintFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, "common", "car_common.tga");

                // Check if the common paint file exists
                if (File.Exists(commonPaintFilePath))
                {
                    // Construct the path to the user-specific paint file
                    string userPaintFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, $"car_{userID}.tga");

                    // Copy the common paint file to the user-specific paint file, overwriting if it already exists
                    File.Copy(commonPaintFilePath, userPaintFilePath, true);
                    Console.WriteLine($"Copied common paint to: {userPaintFilePath}");
                }
                else
                {
                    Console.WriteLine($"Common paint file does not exist: {commonPaintFilePath}");
                    // Handle the absence of the common paint file if necessary
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
        static async Task MonitorQuitAsync(CancellationToken cancellationToken)
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
                await Task.Delay(100, cancellationToken); // Efficiently wait before checking again
            }
        }
    }
}
