﻿/*
 * Copyright 2024 Robertsmania
 * All Rights Reserved
 */
using HerboldRacing; //IRSDKSharper

namespace iRacingEquanimityPaint
{
    class Program
    {
        const int cLoadTimer = 10000;
        const int cUpdateTimer = 100;
        static bool iRacingConnected = false;
        static IRSDKSharper irsdk = new IRSDKSharper();
        static int subSessionID = 0;
        static int driverCarIdx = 0;
        static Dictionary<int, IRacingSdkSessionInfo.DriverInfoModel.DriverModel> driverCache = new Dictionary<int, IRacingSdkSessionInfo.DriverInfoModel.DriverModel>();
        static string documentsFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        static async Task Main(string[] args)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            irsdk.OnSessionInfo += OnSessionInfo;
            irsdk.OnConnected += OnConnected;
            irsdk.OnDisconnected += OnDisconnected;

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

        static async void OnConnected()
        {
            iRacingConnected = true;
            Console.WriteLine("Connected to iRacing");
        }

        static async void OnDisconnected()
        {
            iRacingConnected = false;
            Console.WriteLine("Disconnected from iRacing");
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
        static async void UpdateDriverCache(List<IRacingSdkSessionInfo.DriverInfoModel.DriverModel> currentDriverModels)
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
                    CopyPaint(driverModel.UserID, driverModel.CarPath, "car_common.tga");
                    CopyPaint(driverModel.UserID, driverModel.CarPath, "car_num_common.tga");
                    CopyPaint(driverModel.UserID, driverModel.CarPath, "car_decal_common.tga");
                    CopyPaint(driverModel.UserID, driverModel.CarPath, "helmet_common.tga");
                    CopyPaint(driverModel.UserID, driverModel.CarPath, "suit_common.tga");
                    await Task.Delay(cUpdateTimer); //Delay to let iRacing load completely
                    irsdk.ReloadTextures(IRacingSdkEnum.ReloadTexturesMode.CarIdx, driverModel.CarIdx);
                }
            }
        }

        static void CopyPaint(int userID, string carPath, string commonFileName)
        {
            string userFileName = commonFileName.Replace("common", userID.ToString());
            string commonFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, "common", commonFileName);
            string userFilePath = Path.Combine(documentsFolderPath, "iRacing", "paint", carPath, userFileName);
            
            try
            {
                if (File.Exists(commonFilePath))
                {
                    File.Copy(commonFilePath, userFilePath, true);
                    //Console.WriteLine($"Copied common file to: {userFilePath}");
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
                        if (iRacingConnected)
                        {
                            Console.WriteLine("Forcing a re-run.");
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
    }
}
