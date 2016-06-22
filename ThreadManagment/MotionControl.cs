﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;

using Peak.Can.Basic;
using TPCANHandle = System.UInt16;
using TPCANBitrateFD = System.String;
using TPCANTimestampFD = System.UInt64;

namespace ICDIBasic
{
    public class MotionControl
    {
        [DllImport("Kernel32.dll")]
        private static extern bool QueryPerformanceCounter(out long lpPerformanceCount);
        [DllImport("Kernel32.dll")]
        private static extern bool QueryPerformanceFrequency(out  long lpFrequency);

        [DllImport("kernel32.dll")]
        public static extern bool AllocConsole();

        public const byte WAVE_MODE_DC = 0;
        public const byte WAVE_MODE_SQUARE = 1;
        public const byte WAVE_MODE_TRIANGLE = 2;
        public const byte WAVE_MODE_SINE = 3;
        public const byte WAVE_MODE_USER = 4;

        public const byte WAVE_CONNECT_NON = 0;
        public const byte WAVE_CONNECT_PWM = 1;
        public const byte WAVE_CONNECT_CUR = 2;
        public const byte WAVE_CONNECT_SPD = 3;
        public const byte WAVE_CONNECT_POS = 4;

        PCan pc;
       
        static bool s_bHigh = false;
        static int s_iCount = 0;
        static int s_iCountforwave = 0;
        static int tCount = 0;

        static int gatherCount = 0;

        public delegate void timerEvent();
        public event timerEvent tickEvent;

        private object threadLock = new object();       // for thread safe
        private long clockFrequency;                   // result of QueryPerformanceFrequency() 
        bool running = true;                           //the flag to control the thread
        Thread thread;

        private int intervalMs;                     // interval in mimliseccond;
        private long intevalTicks;
        private long nextTriggerTime;               // the time when next task will be executed

        // Timer inteval in milisecond
        public int Interval
        {
            get { return intervalMs; }
            set
            {
                intervalMs = value;
                intevalTicks = (long)((double)value * (double)clockFrequency / (double)1000);
            }
        }

        public MotionControl()
        {
            pc = new PCan();
            tickEvent = new timerEvent(tick);

            if (QueryPerformanceFrequency(out clockFrequency) == false)
            {
                // Frequency not supported
                throw new Win32Exception("QueryPerformanceFrequency() function is not supported");
            }

            Interval = 1;                      //定时精度 1ms

            thread = new Thread(new ThreadStart(ThreadProc));
            thread.Name = "HighAccuracyTimer";
            thread.Priority = ThreadPriority.Highest;
            thread.Start();
        }

        private void ThreadProc()
        {
            long currTime;
            GetTick(out currTime);
            nextTriggerTime = currTime + intevalTicks;
            while (running)
            {
                while (currTime < nextTriggerTime)
                {
                    GetTick(out currTime);
                }
                nextTriggerTime = currTime + intevalTicks;
                tick();            
            }
        }

        void tick()
        {
            //AllocConsole();
            //System.Console.WriteLine(DateTime.Now.Millisecond.ToString()); 
            if (TestRun.Enablewave)
            {
                double tempf = 0;
                double time = 0;
                double T = 0;

                s_iCount++;
              
                if (TestRun.m_iWaveMode == WAVE_MODE_TRIANGLE || TestRun.m_iWaveMode == WAVE_MODE_SINE)
                {
                     if (s_iCount >= 10)		 //三角波或正弦波时10ms发送一次
                        s_iCount = 0;
                }

                switch (TestRun.m_iWaveMode)
                {
                    case WAVE_MODE_DC:
                        if (s_iCount >= 100)		 //衡值100ms发送一次
                        {
                            s_iCount = 0;
                            SetValue((short)TestRun.m_fBias);
                        }
                        break;
                    case WAVE_MODE_SQUARE:
                        if (s_iCount >= 500.0 / TestRun.m_fFrequency)	//方波时根据选定频率发送
                        {
                            s_iCount = 0;
                            s_bHigh = !s_bHigh;
                            SetValue((short)(TestRun.m_fAmplitude * (s_bHigh ? 1 : -1) + TestRun.m_fBias));
                        }                   
                        break;
                    case WAVE_MODE_TRIANGLE:
                        if (s_iCount == 0)
                        {
                            s_iCountforwave++;
                            time = s_iCountforwave * 10.0f;
                            T = 1000.0f / TestRun.m_fFrequency / 4.0f;
                            if (time <= T)
                            {
                                tempf = time * TestRun.m_fAmplitude / T;
                            }
                            else if (time > T && time <= T * 2)
                            {
                                time -= T;
                                time = T - time;
                                tempf = time * TestRun.m_fAmplitude / T;
                            }
                            else if (time > T * 2 && time <= T * 3)
                            {
                                time -= T * 2;
                                tempf = -time * TestRun.m_fAmplitude / T;
                            }
                            else if (time > T * 3 && time < T * 4)
                            {
                                time -= T * 3;
                                time = T - time;
                                tempf = -time * TestRun.m_fAmplitude / T;
                            }
                            else
                            {
                                tempf = 0;
                                s_iCountforwave = 0;
                            }
                            SetValue((short)(tempf + TestRun.m_fBias));
                        }
                       
                        break;
                    case WAVE_MODE_SINE:
                        if (s_iCount == 0)
                        {
                            s_iCountforwave++;
                            time = s_iCountforwave * 10.0f;
                            T = 1000.0f / TestRun.m_fFrequency;
                            tempf = Math.Sin(time / T * 2 * Math.PI) * TestRun.m_fAmplitude;
                            if (time >= T)
                            {
                                tempf = 0;
                                s_iCountforwave = 0;
                            }
                            SetValue((short)(tempf + TestRun.m_fBias));
                        }
                        break;
                    default:
                        break;
                }
            }
            if (OscilloScope.EnableScope)
            {
                gatherCount++;
                if (gatherCount == OscilloScope.Interval)
                {
                    //AllocConsole();
                    gatherCount = 0;
                    System.Console.WriteLine(DateTime.Now.Millisecond.ToString()); 
                    for (int i = 0; i < OscilloScope.showItems.Count; i++)
                    {
                        if (OscilloScope.showItems[i].sq != null)
                        {
                            // pc.ReadWords(OscilloScope.showItems[i].Item, 2, PCan.currentID);

                            byte[] value1 = BitConverter.GetBytes(Configuration.MemoryControlTable[OscilloScope.showItems[i].Item]);
                            byte[] value2 = BitConverter.GetBytes(Configuration.MemoryControlTable[OscilloScope.showItems[i].Item + 1]);
                            int value = 0;
                            if (OscilloScope.showItems[i].Item == Configuration.SCP_TAGPOS_L || OscilloScope.showItems[i].Item == Configuration.SCP_MEAPOS_L)
                            {
                                value = BitConverter.ToInt32(new byte[] { value1[0], value1[1], 0x00, 0x00 }, 0);
                            }
                            else
                            {
                                value = BitConverter.ToInt32(new byte[] { value1[0], value1[1], value2[0], value2[1] }, 0);
                            }

                            OscilloScope.showItems[i].sq.EnQ(value);
                            //if (OscilloScope.showItems[i].Item == Configuration.SYS_SPEED_L)
                            //{
                            //    System.Console.WriteLine(value.ToString());
                            //}
                        }
                        //if (OscilloScope.showItems[i].Item == Configuration.SYS_POSITION_L)
                        //{
                        //}
                    }
                }
                
            }
            if (OscilloScope.EnableFrictionMeasure)
            {
                tCount++;
                if (tCount % 2 == 0)
                {
                    pc.ReadWords(Configuration.SYS_CURRENT_L, 4, PCan.currentID);
                    double tempf = Math.Sin(tCount * Math.PI / 5000) * 1000 / 60.0 * 65536;
                    pc.WriteTwoWords(Configuration.TAG_SPEED_L, (int)(tempf), PCan.currentID);
                }
                if (tCount >= 3000 && tCount % 2 == 0 && tCount <= 7000)
                {
                    byte[] value1 = BitConverter.GetBytes(Configuration.MemoryControlTable[Configuration.SYS_CURRENT_L]);
                    byte[] value2 = BitConverter.GetBytes(Configuration.MemoryControlTable[Configuration.SYS_CURRENT_L + 1]);
                    float current = BitConverter.ToInt32(new byte[] { value1[0], value1[1], value2[0], value2[1] }, 0);
                    OscilloScope.currentC.Add(current);

                    value1 = BitConverter.GetBytes(Configuration.MemoryControlTable[Configuration.SYS_SPEED_L]);
                    value2 = BitConverter.GetBytes(Configuration.MemoryControlTable[Configuration.SYS_SPEED_L + 1]);
                    float speed = (float)(BitConverter.ToInt32(new byte[] { value1[0], value1[1], value2[0], value2[1] }, 0) * 60.0 / 65536.0);
                    OscilloScope.currentS.Add(speed);
                }
                
                if (tCount == 10000)
                {
                    tCount = 0;
                    //OscilloScope.currentS[0] = (float)(OscilloScope.currentS[0] * Math.PI / 30);
                    //for (int i = 1; i < OscilloScope.currentS.Count;i++ )
                    //{
                    //    OscilloScope.currentS[i] = (float)(OscilloScope.currentS[i] * Math.PI / 30);
                    //    //OscilloScope.currentA.Add((float)((OscilloScope.currentS[i] - OscilloScope.currentS[i - 1]) / 0.002));
                    //}
                    FileStream fs1 = new FileStream("D:a.txt", FileMode.Create, FileAccess.Write, FileShare.None);
                    StreamWriter sw1 = new StreamWriter(fs1, Encoding.Default);
                    //for (int i = 0; i < OscilloScope.currentA.Count; i++)
                    //{
                    //    if (OscilloScope.currentA[i] != 0)
                    //    {
                    //        sw1.WriteLine(OscilloScope.currentA[i].ToString() + " " + Math.Sign(OscilloScope.currentS[i]).ToString() + " " + OscilloScope.currentS[i].ToString() + " " + OscilloScope.currentC[i].ToString());
                    //    }
                    //}
                    for (int i = 0; i < OscilloScope.currentS.Count; i++)
                    {
                        sw1.WriteLine(Math.Sign(OscilloScope.currentS[i]).ToString() + " " + OscilloScope.currentS[i].ToString() + " " + OscilloScope.currentC[i].ToString());
                    }
                    sw1.Flush();
                    sw1.Close();
                    fs1.Close();
                }
              
            }
        }

        void SetValue(short Value)
        {
            switch (TestRun.m_iWaveChannel)
            {
                case WAVE_CONNECT_NON: 
                    break;
                case WAVE_CONNECT_PWM:
                    pc.WriteOneWord(Configuration.TAG_OPEN_PWM, Value, PCan.currentID);
                    break;
                case WAVE_CONNECT_CUR:
                    pc.WriteTwoWords(Configuration.TAG_CURRENT_L, Value, PCan.currentID);
                    break;
                case WAVE_CONNECT_SPD:
                    pc.WriteTwoWords(Configuration.TAG_SPEED_L, Value, PCan.currentID);
                    break;
                case WAVE_CONNECT_POS:
                    pc.WriteTwoWords(Configuration.TAG_POSITION_L, Value, PCan.currentID);
                    break;
                default:
                    break;
            }
        }

        public bool GetTick(out long currentTickCount)
        {
            if (QueryPerformanceCounter(out currentTickCount) == false)
                throw new Win32Exception("QueryPerformanceCounter() failed!");
            else
                return true;
        }

        public void Start()
        {
            thread.Start();
        }
        public void Stop()
        {
            running = false;
        }

        ~MotionControl()
        {
            running = false;
            thread.Abort();
            thread.Join();
            thread = null;
        }







    }
}
