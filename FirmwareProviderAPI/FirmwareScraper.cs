﻿using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Timers;
using System.Xml;
using FirmwareProviderAPI.Messaging;

namespace FirmwareProviderAPI
{
    public class FirmwareScraper : IDisposable
    {
        private const string LegacyApiRequest = "https://wsu-dms.samsungdm.com/common/support/firmware/downloadUrlList.do?prd_mdl_name={0}FOTA&loc=global";
        private readonly string _targetPath;
        
        private readonly Timer _timer;
        private readonly FumoScraper _fumoScraper;

        public FirmwareScraper(string targetPath)
        {
            _fumoScraper = new FumoScraper
            {
                DownloadPath = targetPath
            };

            _targetPath = targetPath;
            _timer = new Timer
            {
                Interval = 600000, // every 10 minutes
                AutoReset = true
            };
            _timer.Elapsed += async (_, _) => await CheckAll();
            _timer.Start();
            
            _ = CheckAll();
        }

        public void Pause()
        {
            _timer.Stop();
        }

        public void Resume()
        {
            _timer.Start();
        }

        public async Task CheckAll()
        {
            CheckForUpdateLegacy("SM-R170");
            CheckForUpdateLegacy("SM-R175");
            await CheckForUpdateFumo("SM-R180");
            await CheckForUpdateFumo("SM-R190");
            await CheckForUpdateFumo("SM-R177");
            await CheckForUpdateFumo("SM-R510");
            await CheckForUpdateFumo("SM-R400N");
            await CheckForUpdateFumo("SM-R530");
            await CheckForUpdateFumo("SM-R630");
        }

        #region OMA-DM FUMO server
        public async Task CheckForUpdateFumo(string model)
        {
            try
            {
                var fwObj = await _fumoScraper.FindUpdate(model);
                if (fwObj == null)
                {
                    return;
                }
                
                await _fumoScraper.DownloadUpdate(fwObj);
            }
            catch (Exception ex)
            {
                Log.E<FirmwareScraper>($"[{model}] Update failed: " + ex);
                Telegram.Send($"[FUMO] Update failed for {model} due to unhandled exception: " + ex);
                return;
            }
        }
        #endregion
        
        #region Legacy DM server
        public bool CheckForUpdateLegacy(string device)
        {
            try
            {
                var reader = new XmlTextReader(string.Format(LegacyApiRequest, device.ToUpper()));
                string? url = null;
                string? name = null;
                while (reader.Read())
                {
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.CDATA:
                            if (reader.Value.Contains("https://"))
                            {
                                url = reader.Value;
                            }

                            break;
                        case XmlNodeType.Text:
                            if (reader.Value.Contains("R1"))
                            {
                                name = $"FOTA_{reader.Value}.bin";
                            }

                            break;
                    }
                }

                if (url == null || name == null)
                {
                    Log.W<FirmwareScraper>("CheckForUpdateLegacy: URL or name null");
                    return false;
                }

                var path = _targetPath + '/' + name;

                if (File.Exists(path))
                {
                    return false;
                }

                using var client = new WebClient();
                client.DownloadFile(new Uri(url), path);
                return true;
            }
            catch (Exception ex)
            {
                Log.W<FirmwareScraper>($"CheckForUpdateLegacy: Exception: {ex}");
                return false;
            }
        }
        #endregion
        
        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}