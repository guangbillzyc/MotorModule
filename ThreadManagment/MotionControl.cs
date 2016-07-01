using System;
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
        public static int tCount = 0;

        static int gatherCount = 0;

        public delegate void timerEvent();
        public event timerEvent tickEvent;

        private object threadLock = new object();       // for thread safe
        private long clockFrequency;                   // result of QueryPerformanceFrequency() 
        bool running = true;                           //the flag to control the thread
        Thread thread;

        private float intervalMs;                     // interval in mimliseccond;
        private long intevalTicks;
        private long nextTriggerTime;               // the time when next task will be executed


        public static double kfQ = 30.0;             // measure noise
        public static double kfR = 0.2;              // system noise

        private double[] kfP = new double[] { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 };
        private double[] kfN = new double[] { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 };
        private double[] kfK = new double[] { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 };

        public delegate void MessageEventHandler(object sender, MessageEventArgs e);
        //定义事件
        public event MessageEventHandler MessageSend;

        public void OnMessageSend(object sender, MessageEventArgs e)
        {
            if (MessageSend != null)
                this.MessageSend(sender, e);
        }


        // Timer inteval in milisecond
        public float Interval
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

            Interval = 0.5f;                      //定时精度 1ms

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
            if (TestRun.EnableRun && TestRun.ChangeOK)
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

                            try
                            {
                                int oldValue = OscilloScope.showItems[i].sq.rear.Value;
                                kfN[i] = kfP[i] + kfQ;
                                kfK[i] = kfN[i] / (kfN[i] + kfR);
                                value = Convert.ToInt32(oldValue + kfK[i] * (value - oldValue));
                                kfP[i] = (1 - kfK[i] * kfN[i]);
                            }
                            catch (System.Exception ex)
                            {
                                //MessageBox.Show("请输入合法的数据!");
                                //MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
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
                float err = 0;
                int speed = getSpeed(tCount,ref err);
                if (Math.Abs(OscilloScope.measureSpeed - speed) < err)
                {
                  
                    FileStream fs1 = new FileStream("D:\\cc.txt", FileMode.Append, FileAccess.Write, FileShare.None);
                    StreamWriter sw1 = new StreamWriter(fs1, Encoding.Default);
                    sw1.WriteLine(speed.ToString() + " " + OscilloScope.measureCurrent.ToString());
                    sw1.Flush();
                    sw1.Close();
                    fs1.Close();
                    this.OnMessageSend(this, new MessageEventArgs("MotionControl", "Measure " + speed + " done!"));
                 
                    tCount++;
                    speed = getSpeed(tCount, ref err);
                    int value = Convert.ToInt32(speed / 60.0 * 65536.0);
                    pc.WriteTwoWords(Configuration.TAG_SPEED_L, value, PCan.currentID);

                    if (tCount == 94)
                    {
                        OscilloScope.EnableFrictionMeasure = false;
                        MessageBox.Show("Measure process finished!");
                    }
                }
                //if (tCount >= 100 && tCount <= 2300)
                //{
                //    //电流
                //    byte[] value1 = BitConverter.GetBytes(Configuration.MemoryControlTable[Configuration.SCP_MEACUR_L]);
                //    byte[] value2 = BitConverter.GetBytes(Configuration.MemoryControlTable[Configuration.SCP_MEACUR_H]);
                //    float current = BitConverter.ToInt32(new byte[] { value1[0], value1[1], value2[0], value2[1] }, 0);
                //    OscilloScope.currentC.Add(0.118f * current / 1000.0f);

                //    //速度
                //    value1 = BitConverter.GetBytes(Configuration.MemoryControlTable[Configuration.SCP_MEASPD_L]);
                //    value2 = BitConverter.GetBytes(Configuration.MemoryControlTable[Configuration.SCP_MEASPD_H]);
                //    float speed = (float)(BitConverter.ToInt32(new byte[] { value1[0], value1[1], value2[0], value2[1] }, 0) * 2.0 * Math.PI / 65536.0); // rad/s
                //    OscilloScope.currentS.Add(speed);

                //    OscilloScope.currentT.Add(DateTime.Now.Millisecond);
                //}
                
                //if (tCount == 2300)
                //{
                //    tCount = 0;
                //    for (int i = 0; i < OscilloScope.currentS.Count; i++)
                //    {
                //        sw1.WriteLine(Math.Sign(OscilloScope.currentS[i]).ToString() + " " + OscilloScope.currentS[i].ToString() + " " 
                //                      + OscilloScope.currentC[i].ToString() + " " + OscilloScope.currentT[i].ToString());
                //    }
                //    OscilloScope.EnableFrictionMeasure = false;
                //}
              
            }

            if (OscilloScope.EnableCurrentCompensation)
            {
                if(OscilloScope.measureSpeed < 0.5)
                {
                    pc.WriteTwoWords(Configuration.TAG_CURRENT_L, 192, PCan.currentID);
                }
                else if(OscilloScope.measureSpeed < 95)
                {
                    int value = Convert.ToInt32(156.0648 - (OscilloScope.measureSpeed - 1) / 94.0 * (156.0648 - 146.6248));
                    pc.WriteTwoWords(Configuration.TAG_CURRENT_L, value, PCan.currentID);
                }
                else
                {
                    int value = Convert.ToInt32(-0.000036681545 * OscilloScope.measureSpeed * OscilloScope.measureSpeed + 0.22859848 * OscilloScope.measureSpeed + 126.16865);
                }
            }
        }

        void SetValue(int value)
        {
            switch (TestRun.m_iWaveChannel)
            {
                case WAVE_CONNECT_NON: 
                    break;
                case WAVE_CONNECT_PWM:
                    pc.WriteOneWord(Configuration.TAG_OPEN_PWM, (short)value, PCan.currentID);
                    break;
                case WAVE_CONNECT_CUR:
                    pc.WriteTwoWords(Configuration.TAG_CURRENT_L, value, PCan.currentID);
                    break;
                case WAVE_CONNECT_SPD:
                    value = Convert.ToInt32(value / 60.0 * 65536.0);
                    pc.WriteTwoWords(Configuration.TAG_SPEED_L, value, PCan.currentID);
                    break;
                case WAVE_CONNECT_POS:
                    value = Convert.ToInt32(value / 360.0 * 65535.0);
                    pc.WriteTwoWords(Configuration.TAG_POSITION_L, value, PCan.currentID);
                    break;
                default:
                    break;
            }
        }

        int getSpeed(int i, ref float err)
        {
            if (i <= 30)
            {
                err = 3;            //1500 - 600
                return (30 - i) * 30 + 600;
            }
            else if (i <= 45)
            {
                err = 2;
                return (45 - i) * 20 + 300;
            }                         //600 - 300
            else if (i <= 64)
            {
                err = 1;                //300 - 110
                return (64 - i) * 10 + 110;
            }                        
            else if (i <= 84)
            {
                err = 0.5f;              //110 - 10   
                return (84 - i) * 5 + 10;                          
            }
            else if (i <= 93)
            {
                err = 0.1f;
                return (94 - i);                            //10 - 1
            }

            return 0;
        }

        public bool GetTick(out long currentTickCount)
        {
            if (QueryPerformanceCounter(out currentTickCount) == false)
            {
                throw new Win32Exception("QueryPerformanceCounter() failed!");
            }
            else
            {
                return true;
            } 
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
