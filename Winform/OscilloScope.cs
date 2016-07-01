using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
//using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.IO;

namespace ICDIBasic
{
    public partial class OscilloScope : Form
    {
        public static OscilloScope pCurrentWin = null;//句柄
        public static List<ShowItem> showItems = new List<ShowItem>();
        public static byte Mask = 0; 
        public static int Interval = 0;
        Dictionary<byte, string> itemRelection = new Dictionary<byte, string>();
        ComboBox cb;
        Thread thread;
        PCan pc;
        bool running = true;
        int spanTime = 20;
        double speedRatio = 60.0 / 65536;
        double positionRatio = 360.0 / 65535;
        double currentRatio = 1.0;
        float[] pointA;

        double cr;
        double sr;
        double pr;

        float currentOffset;
        float speedOffset;
        float positionOffset;

        int tracePos1 = 0;
        int tracePos2 = 0;
        public static bool EnableScope = false;

        public static bool EnableFrictionMeasure = false;
        public static bool EnableCurrentCompensation = false;

        public static List<float> currentC = new List<float>();
        public static List<float> currentS = new List<float>();
        public static List<int> currentT = new List<int>();
        public static List<float> currentA = new List<float>();


        public static byte CURRENT_P = 0x61;	  //电流环P参数
        public static byte CURRENT_I = 0x62;	  //电流环I参数
        public static byte CURRENT_D = 0x63;	  //电流环D参数
        public static byte SPEED_P = 0x64;	  //速度环P参数
        public static byte SPEED_I = 0x65;	  //速度环I参数
        public static byte SPEED_D = 0x66;	  //速度环D参数
        public static byte SPEED_DS = 0x67;	  //速度P死区
        public static byte POSITION_P = 0x68;	  //位置环P参数
        public static byte POSITION_I = 0x69;	  //位置环I参数
        public static byte POSITION_D = 0x6A;	  //位置环D参数
        public static byte POSITION_DS = 0x6B;    //位置P死区


        public static float measureCurrent = 0;          //实际电流
        public static float measureSpeed = 0;          //实际电流

        public OscilloScope()
        {
            InitializeComponent();
            pc = new PCan();

            InitialControls();
            thread = new Thread(new ThreadStart(ThreadProc));
            thread.Name = "HighAccuracyTimer";
            //thread.Start();
            timerPaint.Start();
        }

        ~OscilloScope()
        {
            running = false;
            thread.Abort();
            thread.Join();
            thread = null;
        }

        public static OscilloScope GetInstance()
        {
            if (pCurrentWin == null)
            {
                pCurrentWin = new OscilloScope();
            }
            return pCurrentWin;
        }

        private void OscilloScope_FormClosed(object sender, FormClosedEventArgs e)
        {
            pCurrentWin = null;
            timerPaint.Stop();
        }

        private void OscilloScope_FormClosing(object sender, FormClosingEventArgs e)
        {
            running = false;
            Thread.Sleep(spanTime + 5);   //等待线程thread结束后再abort
            if (thread != null)
            {
                thread.Abort();
                //thread.Join();
                thread = null;
            }
        }

        private void InitialControls()
        {
            tracePos1 = 0;
            tracePos2 = pLPaint.Width;
            itemRelection.Clear();
            itemRelection.Add(Configuration.SCP_TAGCUR_L, "指令电流");
            itemRelection.Add(Configuration.SCP_TAGSPD_L, "指令速度");
            itemRelection.Add(Configuration.SCP_TAGPOS_L, "指令位置");
            itemRelection.Add(Configuration.SCP_MEACUR_L, "实际电流");
            itemRelection.Add(Configuration.SCP_MEASPD_L, "实际速度");
            itemRelection.Add(Configuration.SCP_MEAPOS_L, "实际位置");
            showItems.Clear();
            Mask = (byte)Configuration.MemoryControlTable[Configuration.SCP_MASK];
            showItems.Add(new ShowItem((Mask & Configuration.MASK_TAGCUR) != 0x00, Configuration.SCP_TAGCUR_L, "观测", Color.Yellow, DashStyle.Dot, pLPaint.Width, Configuration.MASK_TAGCUR));
            showItems.Add(new ShowItem((Mask & Configuration.MASK_TAGSPD) != 0x00, Configuration.SCP_TAGSPD_L, "观测", Color.Green, DashStyle.Solid, pLPaint.Width, Configuration.MASK_TAGSPD));
            showItems.Add(new ShowItem((Mask & Configuration.MASK_TAGPOS) != 0x00, Configuration.SCP_TAGPOS_L, "观测", Color.Blue, DashStyle.Dash, pLPaint.Width, Configuration.MASK_TAGPOS));
            showItems.Add(new ShowItem((Mask & Configuration.MASK_MEACUR) != 0x00, Configuration.SCP_MEACUR_L, "观测", Color.Cyan, DashStyle.Dot, pLPaint.Width, Configuration.MASK_MEACUR));
            showItems.Add(new ShowItem((Mask & Configuration.MASK_MEASPD) != 0x00, Configuration.SCP_MEASPD_L, "观测", Color.HotPink, DashStyle.Solid, pLPaint.Width, Configuration.MASK_MEASPD));
            showItems.Add(new ShowItem((Mask & Configuration.MASK_MEAPOS) != 0x00, Configuration.SCP_MEAPOS_L, "观测", Color.Red, DashStyle.Dash, pLPaint.Width, Configuration.MASK_MEAPOS));

            //Mask |= Configuration.MASK_TAGSPD | Configuration.MASK_MEASPD | Configuration.MASK_TAGPOS | Configuration.MASK_MEAPOS;
            //pc.WriteOneWord(Configuration.SCP_MASK, OscilloScope.Mask, PCan.currentID);    //应设置触发条件

            //设置扫描频率
            tBScanFrequency.Text = Configuration.MemoryControlTable[Configuration.SCP_REC_TIM].ToString();
            setTimeInterval(Configuration.MemoryControlTable[Configuration.SCP_REC_TIM]);


            pointA = new float[pLPaint.Width];
            for (int i = 0; i < pLPaint.Width; i++)
            {
                pointA[i] = i;
            }

            loadLVMeasureItems();

            cBCurrentRatio.Text = IniFile.ContentValue("plRange", "Current", IniFile.StrProPath);
            cBSpeedRatio.Text = IniFile.ContentValue("plRange", "Speed", IniFile.StrProPath);
            cBPositionRatio.Text = IniFile.ContentValue("plRange", "Position", IniFile.StrProPath);
            cr = Convert.ToDouble(IniFile.ContentValue("plRange", "Current", IniFile.StrProPath));
            sr = Convert.ToDouble(IniFile.ContentValue("plRange", "Speed", IniFile.StrProPath));
            pr = Convert.ToDouble(IniFile.ContentValue("plRange", "Position", IniFile.StrProPath));
            currentOffset = 0.0f;
            speedOffset = 0.0f; ;
            positionOffset = 0.0f;
            tMPointer.Start();
        }

        void setTimeInterval(short interval)
        {
            Interval = interval / 10;
            //short value = (short)(interval * 10);
            pc.WriteOneWord(Configuration.SCP_REC_TIM, interval, PCan.currentID);
        }




        private void ThreadProc()
        {
            //while (running)
            //{
            //    thread.Join(spanTime);
            //    pc.ReadWords(Configuration.SYS_CURRENT_L, 6, PCan.currentID);
            //    //添加元素到显示队列
            //    for (int i = 0; i < showItems.Count;i++)
            //    {
            //        if (showItems[i].sq != null)
            //        {
            //            showItems[i].sq.EnQ(Configuration.MemoryControlTable[showItems[i].Item] + (int)(Configuration.MemoryControlTable[showItems[i].Item + 1] << 16));
            //        }
                  
            //    }
            //}

        }

        private void pLPaint_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Font font = new Font("微软雅黑", 16F);
            SolidBrush brush = new SolidBrush(Color.Red);
           // g.Clear(Color.Black);

            Pen pen = new Pen(Color.DarkGray, 1);
            pen.DashStyle = DashStyle.Dash;
            for (int i = 1; i < 12;i++)
            {
                g.DrawLines(pen, new Point[] { new Point(0, i * 30), new Point(740, i * 30) });
            }
            pen.DashStyle = DashStyle.Custom;
            pen.Width = 1.8f;
            g.DrawLines(pen, new Point[] { new Point(0, 6 * 30), new Point(740, 6 * 30) });

            if (cBPointer.Checked)
            {
                pen.Color = Color.Cyan;
                g.DrawLines(pen, new Point[] { new Point(tracePos1, 0), new Point(tracePos1, 360) });
                pen.Color = Color.DarkCyan;
                g.DrawLines(pen, new Point[] { new Point(tracePos2, 0), new Point(tracePos2, 360) });
            }
          

            for (int i = 0; i < showItems.Count;i++ )
            {
                if (showItems[i].sq != null && showItems[i].IsCheck)
                {
                    int[] pointY = new int[showItems[i].sq.Count()];
                    showItems[i].sq.Print(ref pointY);
                    //MovingAverage(ref pointY, 0);
                    //int maxY = GetMax(pointY);
                    int m = pointY.Length;
                    if (m > 1)
                    {
                        PointF[] points = new PointF[m];
                      
                        if (showItems[i].Item ==   Configuration.SCP_TAGCUR_L || showItems[i].Item == Configuration.SCP_MEACUR_L)
                        {
                            for (int j = 0; j < m; j++)
                            {
                                points[j] = new PointF(1.0f * pointA[j], (float)(pLPaint.Height / 2 - pointY[j] * currentRatio / cr * 30 - currentOffset));
                            }
                        }
                        else if (showItems[i].Item ==   Configuration.SCP_TAGSPD_L || showItems[i].Item ==  Configuration.SCP_MEASPD_L)
                        {
                            for (int j = 0; j < m; j++)
                            {
                                points[j] = new PointF(1.0f * pointA[j], (float)(pLPaint.Height / 2 - pointY[j] * speedRatio / sr * 30 - speedOffset));
                            }
                        }
                        else if (showItems[i].Item == Configuration.SCP_TAGPOS_L || showItems[i].Item == Configuration.SCP_MEAPOS_L)
                        {
                            for (int j = 0; j < m; j++)
                            {
                                points[j] = new PointF(1.0f * pointA[j], (float)(pLPaint.Height / 2 - pointY[j] * positionRatio / pr * 30 - positionOffset));
                            }
                        }
                      
                        pen.Color = showItems[i].Cl;
                        pen.DashStyle = showItems[i].Ds;
                        g.DrawLines(pen, points);
                    }
                }
            }
        }

        void MovingAverage(ref int[] array, int n)
        {
            try
            {
                for (int i = n / 2; i < array.Length - n / 2; i++)
                {
                    int sum = 0;
                    for (int j = 0; j < n; j++)
                    {
                        sum += array[i - n / 2 + j];
                    }
                    array[i] = sum / n;
                }
            }
            catch (System.Exception ex)
            {
                MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
            }
           
        }

        private static int GetMax(int[] array)
        {
            int max = 0;
            for (int i = 0; i < array.Length; i++)
            {
                max = max > Math.Abs(array[i]) ? max : Math.Abs(array[i]);

            }
            return max;
        }

        private void timerPaint_Tick(object sender, EventArgs e)
        {
            pLPaint.Refresh();

        }

        //指针响应处理函数
        private void tBtrace_Scroll(object sender, EventArgs e)
        {
            if (rBPointer1.Checked)
            {
                tracePos1 = (ushort)tBtrace.Value;
                if (tracePos1 >= tracePos2)
                {
                    tracePos1 = tracePos2;
                    tBtrace.Value = tracePos1;
                }
            }
            if (rBPointer2.Checked)
            {
                tracePos2 = (ushort)tBtrace.Value - 1;
                if (tracePos1 >= tracePos2)
                {
                    tracePos2 = tracePos1;
                    tBtrace.Value = tracePos2;
                }
            }
        }

        private void tCMonitor_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (tCMonitor.SelectedIndex)
            {
                case 0: loadLVMeasureItems(); break;
                case 3: cBAdjustGroup.Text = Configuration.MemoryControlTable[Configuration.SEV_PARAME_LOCKED].ToString(); loadPID(); break; 
                case 2: if (cBPointer.Checked) loadlVPointer(); break;
            }
        }

        void loadPID()
        {
            string pidGroup = IniFile.ContentValue("OscilloScope", "PIDGroup", IniFile.StrProPath);
            cBAdjustGroup.Text = pidGroup;
            
            Thread.Sleep(5);

            tBCurrentP.Text = Configuration.MemoryControlTable[CURRENT_P].ToString();
            tBCurrentI.Text = Configuration.MemoryControlTable[CURRENT_I].ToString();
            tBSpeedP.Text = Configuration.MemoryControlTable[SPEED_P].ToString();
            tBSpeedI.Text = Configuration.MemoryControlTable[SPEED_I].ToString();
            tBSpeedD.Text = Configuration.MemoryControlTable[SPEED_D].ToString();
            tBSpeedDeadZone.Text = Configuration.MemoryControlTable[SPEED_DS].ToString();
            tBPosP.Text = Configuration.MemoryControlTable[POSITION_P].ToString();
            tBPosI.Text = Configuration.MemoryControlTable[POSITION_I].ToString();
            tBPosD.Text = Configuration.MemoryControlTable[POSITION_D].ToString();
            tBPosDeadZone.Text = Configuration.MemoryControlTable[POSITION_DS].ToString();
            tBMaxCurrent.Text = Configuration.MemoryControlTable[Configuration.LIT_MAX_CURRENT].ToString();
            tBMaxSpeed.Text = Configuration.MemoryControlTable[Configuration.LIT_MAX_SPEED].ToString();
            tBMaxAcc.Text = Configuration.MemoryControlTable[Configuration.LIT_MAX_ACC].ToString();

           
        }

        void loadLVMeasureItems()
        {
            lVMeasureItems.Items.Clear();
            lVMeasureItems.Visible = true;
            for (int i = 0; i < showItems.Count;i++ )
            {
                lVMeasureItems.Items.Add((i+1).ToString());
                lVMeasureItems.Items[i].SubItems.AddRange(new string[] { itemRelection[showItems[i].Item], showItems[i].Monitor, "", GetDashStyle(showItems[i].Ds) });
                lVMeasureItems.Items[i].UseItemStyleForSubItems = false;
                lVMeasureItems.Items[i].SubItems[3].BackColor = showItems[i].Cl;
                //lVFormat.Items[i].Focused = true;
                lVMeasureItems.Items[i].Checked = showItems[i].IsCheck;
            }
        }

        private string GetDashStyle(DashStyle ds)
        {
            string str = "";
            switch (ds)
            {
                case DashStyle.Solid: str = "实线"; break;
                case DashStyle.Dash: str ="虚线" ; break;
                case DashStyle.Dot: str = "点线"; break;
                case DashStyle.DashDot: str = "点划线"; break;
                case DashStyle.DashDotDot : str = "双点划线"; break;
            }
            return str;
        }

        private void lVFormat_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (lVMeasureItems.FocusedItem != null)
            {
                int index = e.Item.Index;
                e.Item.Selected = e.Item.Checked;
                showItems[index].IsCheck = e.Item.Checked;
                if (showItems[index].IsCheck)
                {
                    Mask |= showItems[index].Mask;
                }
                else
                {
                    Mask &= (byte) ~(int)showItems[index].Mask;
                }

                pc.WriteOneWord(Configuration.SCP_MASK, OscilloScope.Mask, PCan.currentID);       //向下位机请求数据
            }
        }

        private void tBCurrentP_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (e.KeyCode == Keys.Enter)
            {
                short value = 0;
                try
                {
                     value = Convert.ToInt16(tb.Text);
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show("请输入合法的字符串！");
                    MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
                    return;
                }
                switch(tb.Name)
                {
                    case "tBCurrentP":pc.WriteOneWord(CURRENT_P,value,PCan.currentID); break;
                    case "tBCurrentI":pc.WriteOneWord(CURRENT_I,value,PCan.currentID);  break;
                    case "tBSpeedP":pc.WriteOneWord(SPEED_P,value,PCan.currentID);  break;
                    case "tBSpeedI":pc.WriteOneWord(SPEED_I,value,PCan.currentID);  break;
                    case "tBSpeedD":pc.WriteOneWord(SPEED_D,value,PCan.currentID);  break;
                    case "tBSpeedDeadZone": pc.WriteOneWord(SPEED_DS,value,PCan.currentID); break;
                    case "tBPosP":pc.WriteOneWord(POSITION_P,value,PCan.currentID);  break;
                    case "tBPosI": pc.WriteOneWord(POSITION_I,value,PCan.currentID); break;
                    case "tBPosD": pc.WriteOneWord(POSITION_D,value,PCan.currentID); break;
                    case "tBPosDeadZone": pc.WriteOneWord(POSITION_DS,value,PCan.currentID); break; 

                    case "tBMaxCurrent": pc.WriteOneWord(Configuration.LIT_MAX_CURRENT,value,PCan.currentID); break; 
                    case "tBMaxSpeed": pc.WriteOneWord(Configuration.LIT_MAX_SPEED,value,PCan.currentID); break; 
                    case "tBMaxAcc": pc.WriteOneWord(Configuration.LIT_MAX_ACC,value,PCan.currentID); break;

                    case "tBScanFrequency": setTimeInterval(value);break;
                }
            }
        }

        private void cBAdjustGroup_SelectedIndexChanged(object sender, EventArgs e)
        {
            string type = cBAdjustGroup.Text;
            switch (type)
            {
                case "1": CURRENT_P = Configuration.S_CURRENT_P;
                    CURRENT_I = Configuration.S_CURRENT_I;
                    CURRENT_D = Configuration.S_CURRENT_D;
                    SPEED_P = Configuration.S_SPEED_P;
                    SPEED_I = Configuration.S_SPEED_I;
                    SPEED_D = Configuration.S_SPEED_D;
                    SPEED_DS = Configuration.S_SPEED_DS;
                    POSITION_P = Configuration.S_POSITION_P;
                    POSITION_I = Configuration.S_POSITION_I;
                    POSITION_D = Configuration.S_POSITION_D;
                    POSITION_DS = Configuration.S_POSITION_DS;
                    break;
                case "2": CURRENT_P = Configuration.M_CURRENT_P;
                    CURRENT_I = Configuration.M_CURRENT_I;
                    CURRENT_D = Configuration.M_CURRENT_D;
                    SPEED_P = Configuration.M_SPEED_P;
                    SPEED_I = Configuration.M_SPEED_I;
                    SPEED_D = Configuration.M_SPEED_D;
                    SPEED_DS = Configuration.M_SPEED_DS;
                    POSITION_P = Configuration.M_POSITION_P;
                    POSITION_I = Configuration.M_POSITION_I;
                    POSITION_D = Configuration.M_POSITION_D;
                    POSITION_DS = Configuration.M_POSITION_DS;
                    break;
                case "3": CURRENT_P = Configuration.L_CURRENT_P;
                    CURRENT_I = Configuration.L_CURRENT_I;
                    CURRENT_D = Configuration.L_CURRENT_D;
                    SPEED_P = Configuration.L_SPEED_P;
                    SPEED_I = Configuration.L_SPEED_I;
                    SPEED_D = Configuration.L_SPEED_D;
                    SPEED_DS = Configuration.L_SPEED_DS;
                    POSITION_P = Configuration.L_POSITION_P;
                    POSITION_I = Configuration.L_POSITION_I;
                    POSITION_D = Configuration.L_POSITION_D;
                    POSITION_DS = Configuration.L_POSITION_DS;
                    break;
            }
            if (type == "0")
            {
                gBCurrent.Enabled = false;
                gBSpeed.Enabled = false;
                gBPos.Enabled = false;
            }
            else
            {
                gBCurrent.Enabled = true;
                gBSpeed.Enabled = true;
                gBPos.Enabled = true;
            }
            pc.WriteOneWord(Configuration.SEV_PARAME_LOCKED, Convert.ToInt16(type), PCan.currentID);    //应设置触发条件
            IniFile.WritePrivateProfileString("OscilloScope", "PIDGroup", type, IniFile.StrProPath);
            loadPID();
        }

        private void tBCurrentP_TextChanged(object sender, EventArgs e)
        {
            TextBox tb = sender as TextBox;
            try
            {
                short value = Convert.ToInt16(tb.Text);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("请输入合法的字符串！");
                MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
                return;
            }
        }

        private void cBPointer_CheckedChanged(object sender, EventArgs e)
        {
            if (cBPointer.Checked)
            {
                tBtrace.Enabled = true;
                if (rBPointer1.Checked)
                {
                    tBtrace.Value = tracePos1;
                }
                else
                {
                    tBtrace.Value = tracePos2;
                }
            
                loadlVPointer();
            }
            else
            {
                //tMPointer.Stop();
                tBtrace.Enabled = false;
                lVPointer.Items.Clear();
            }
        }

        private void refreshlVPointer()
        {
            int j = 0;
            for (int i = 0; i < showItems.Count; i++)
            {
                if (showItems[i].IsCheck && showItems[i].sq != null)
                {
                    double ratio = 1.0;
                    if(showItems[i].Item == Configuration.SCP_TAGSPD_L || showItems[i].Item == Configuration.SCP_MEASPD_L)
                    {
                        ratio = 60.0 / 65536.0;
                    }
                    try
                    {
                        int pV1 = Convert.ToInt32(GetPointerValue(showItems[i].sq, tracePos1) * ratio);
                        int pV2 = Convert.ToInt32(GetPointerValue(showItems[i].sq, tracePos2) * ratio);
                        float pAverage = Convert.ToSingle(showItems[i].sq.GetAverageValue(tracePos1, tracePos2) * ratio);
                        float maxValue = Convert.ToSingle(showItems[i].sq.GetMaxValue(tracePos1, tracePos2) * ratio);
                        float minValue = Convert.ToSingle(showItems[i].sq.GetMinValue(tracePos1, tracePos2) * ratio);
                        lVPointer.Items[j].SubItems[1].Text = "";
                        lVPointer.Items[j].SubItems[2].Text = pV1.ToString();
                        lVPointer.Items[j].SubItems[3].Text = pV2.ToString();
                        lVPointer.Items[j].SubItems[4].Text = (pV2 - pV1).ToString();
                        lVPointer.Items[j].SubItems[5].Text = pAverage.ToString();
                        lVPointer.Items[j].SubItems[6].Text = maxValue.ToString();
                        lVPointer.Items[j].SubItems[7].Text = minValue.ToString();
                        j++;

                        //取出实际电流、实际速度值，用于计算摩擦参数
                        if(showItems[i].Item == Configuration.SCP_MEASPD_L)
                        {
                            measureSpeed = pAverage;
                        }
                        else if(showItems[i].Item == Configuration.SCP_MEACUR_L)
                        {
                            measureCurrent = pAverage;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
                    }

                }
            }
        }

        private void loadlVPointer()
        {
            lVPointer.Items.Clear();
            int j = 0;
            for (int i = 0; i < showItems.Count; i++)
            {
                if (showItems[i].IsCheck && showItems[i].sq != null)
                {

                    lVPointer.Items.Add(itemRelection[showItems[i].Item]);
                    int pV1 = GetPointerValue(showItems[i].sq, tracePos1);
                    int pV2 = GetPointerValue(showItems[i].sq, tracePos2);
                    float pAverage = showItems[i].sq.GetAverageValue(tracePos1, tracePos2);
                    float maxValue = showItems[i].sq.GetMaxValue(tracePos1, tracePos2);
                    float minValue = showItems[i].sq.GetMinValue(tracePos1, tracePos2);
                    lVPointer.Items[j].SubItems.AddRange(new string[] { "", pV1.ToString(), pV2.ToString(), (pV2 - pV1).ToString(), pAverage.ToString(), maxValue.ToString(), minValue.ToString() });
                    j++;
                }

            }
        }

        private int GetPointerValue(ShowQueue sq, int index)
        {
            if (index >= sq.Count())
            {
                return 0;
            }
            else
            {
                return sq.GetValue(index);
            }
        }

        private void rBPointer1_CheckedChanged(object sender, EventArgs e)
        {
            if (rBPointer1.Checked)
            {
                tBtrace.Value = tracePos1;
            }
            else
            {
                tBtrace.Value = tracePos2;
            }
        }

        private void btnMeasure_Click(object sender, EventArgs e)
        {
            if (btnMeasure.Text == "测定")
            {
                btnMeasure.Text = "停止";
                btnMeasure.BackColor = Color.Red;
                EnableScope = true;
                pBRecordImage.Enabled = true;
            }
            else
            {
                btnMeasure.Text = "测定";
                btnMeasure.BackColor = Color.Green;
                EnableScope = false;
                pBRecordImage.Enabled = false;
            }
        }

        private void lVFormat_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (e.X > 420 && e.X < 570)
                {
                    int index = lVMeasureItems.FocusedItem.Index;
                    if (cDcolor.ShowDialog() == DialogResult.OK)
                    {
                        showItems[index].Cl = cDcolor.Color;
                        loadLVMeasureItems();
                    }
                }
                else if (e.X > 570 && e.X < 740)
                {
                    int index = lVMeasureItems.FocusedItem.Index;
                    Rectangle tt = lVMeasureItems.Items[index].SubItems[4].Bounds;
                    if (cb != null)
                    {
                        showItems[(int)cb.Tag].Ds = GetDashStyle(cb.Text);
                        loadLVMeasureItems();
                        lVMeasureItems.Controls.Remove(cb);
                        cb = null;
                    }
                    cb = new ComboBox();
                    cb.Tag = index;
                    cb.Font = new Font("宋体", 13, FontStyle.Bold);
                    cb.Size = new Size(tt.Width, 18);
                    cb.Location = tt.Location;
                    cb.BackColor = Color.White;
                    cb.Text = "实线";
                    cb.Items.Add("实线");
                    cb.Items.Add("虚线");
                    cb.Items.Add("点线");
                    cb.Items.Add("点划线");
                    cb.Items.Add("双点划线");
                   
                   // cb.Focus();
                    lVMeasureItems.Controls.Add(cb);
                }

            }
            else if (e.Button == MouseButtons.Left)
            {
                try
                {
                    int index = lVMeasureItems.SelectedItems[0].Index;
                    if (cb != null && ((object)index != cb.Tag || e.X < 570 || e.X > 740))  //注意判定条件
                    {
                        showItems[(int)cb.Tag].Ds = GetDashStyle(cb.Text);
                        loadLVMeasureItems();
                        lVMeasureItems.Controls.Remove(cb);
                        cb = null;
                    }
                }
                catch (System.Exception ex)
                {
                    MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
                }
            }
           
           
        }

        private DashStyle GetDashStyle(string strStyle)
        {
            DashStyle ds = new DashStyle();
            switch (strStyle)
            {
                case "实线": ds = DashStyle.Solid;break;
                case "虚线": ds = DashStyle.Dash; break;
                case "点线": ds = DashStyle.Dot; break;
                case "点划线": ds = DashStyle.DashDot; break;
                case "双点划线": ds = DashStyle.DashDotDot; break;
            }
            return ds;
        }

        private void tMPointer_Tick(object sender, EventArgs e)
        {
            if (cBPointer.Checked)
            {
                refreshlVPointer();
            }
            if (!cBCurrentRatio.Focused)
            {
                cBCurrentRatio.Text = cr.ToString() + "mA";
            }
            if (!cBSpeedRatio.Focused)
            {
                cBSpeedRatio.Text = sr.ToString() + "rpm";
            }
            if (!cBPositionRatio.Focused)
            {
                cBPositionRatio.Text = pr.ToString() + "°";
            }
           
        }

        private void pBRecordImage_Click(object sender, EventArgs e)
        {
            //记录当前波形

        }

        private void cBCurrentRatio_SelectedIndexChanged(object sender, EventArgs e)
        {
            ComboBox cb = sender as ComboBox;
            if (cb.Name == "cBCurrentRatio")
            {
                cr = Convert.ToDouble(cBCurrentRatio.Text.Substring(0, cBCurrentRatio.Text.Length - 2));
                IniFile.WritePrivateProfileString("plRange", "Current", cr.ToString(), IniFile.StrProPath);
            }
            else if (cb.Name == "cBSpeedRatio")
            {
                sr = Convert.ToDouble(cBSpeedRatio.Text.Substring(0, cBSpeedRatio.Text.Length - 3));
                IniFile.WritePrivateProfileString("plRange", "Speed", sr.ToString(), IniFile.StrProPath);
            }
            else if (cb.Name == "cBPositionRatio")
            {
                pr = Convert.ToDouble(cBPositionRatio.Text.Substring(0, cBPositionRatio.Text.Length - 1));
                IniFile.WritePrivateProfileString("plRange", "Position", pr.ToString(), IniFile.StrProPath);
            }
            btnMeasure.Focus();
        }


        private void cBCurrentRatio_KeyDown(object sender, KeyEventArgs e)
        {

            if (e.KeyCode == Keys.Enter)
            {
                ComboBox cb = sender as ComboBox;
                try
                {
                    if (cb.Name == "cBCurrentRatio")
                    {
                        cr = Convert.ToDouble(cb.Text.Substring(0, cb.Text.Length - 2));
                        IniFile.WritePrivateProfileString("plRange", "Current", cr.ToString(), IniFile.StrProPath);
                    }
                    else if (cb.Name == "cBSpeedRatio")
                    {
                        sr = Convert.ToDouble(cb.Text.Substring(0, cb.Text.Length - 3));
                        IniFile.WritePrivateProfileString("plRange", "Speed", sr.ToString(), IniFile.StrProPath);
                    }
                    else if (cb.Name == "cBPositionRatio")
                    {
                        pr = Convert.ToDouble(cb.Text.Substring(0, cb.Text.Length - 1));
                        IniFile.WritePrivateProfileString("plRange", "Position", pr.ToString(), IniFile.StrProPath);
                    }
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show("请输入合法的数据!");
                    MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
                }
            }
        }

        private void cBCurrentRatio_MouseEnter(object sender, EventArgs e)
        {
            ComboBox cb = sender as ComboBox;
            cb.Focus();
            if (cb.Name == "cBCurrentRatio")
            {
                cb.Select(0, cb.Text.Length - 2);
            }
            else if (cb.Name == "cBSpeedRatio")
            {
                cb.Select(0, cb.Text.Length - 3);
            }
            else if (cb.Name == "cBPositionRatio")
            {
                cb.Select(0, cb.Text.Length - 1);
            }
        }

        private void tBCurrentOffset_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                try
                {
                    TextBox tb = sender as TextBox;
                    if (tb.Name == "tBCurrentOffset")
                    {
                        currentOffset = Convert.ToSingle(tBCurrentOffset.Text);
                    }
                    else if (cb.Name == "tBSpeedOffset")
                    {
                        speedOffset = Convert.ToSingle(tBSpeedOffset.Text);
                    }
                    else if (cb.Name == "tBPositionOffset")
                    {
                        positionOffset = Convert.ToSingle(tBPositionOffset.Text);
                    }
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show("请输入合法的数据!");
                    MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
                }
            }
        }

        private void btnFriction_Click(object sender, EventArgs e)
        {
            pc.WriteOneWord(Configuration.TAG_WORK_MODE, Configuration.MODE_SPEED, PCan.currentID);
            int value = Convert.ToInt32(1500 / 60.0 * 65536.0);
            pc.WriteTwoWords(Configuration.TAG_SPEED_L, value, PCan.currentID);

            OscilloScope.Mask = 10;
            pc.WriteOneWord(Configuration.SCP_MASK, OscilloScope.Mask, PCan.currentID);       //打开电流 速度发送开关
            MotionControl.tCount = 0;
            EnableFrictionMeasure = true;
        }

        private void btnCurrentCompensation_Click(object sender, EventArgs e)
        {
            EnableCurrentCompensation = true;
        }

        #region 用鼠标拖拽移动窗体
        private Point mousePoint = Point.Empty;
        private void pLName_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                //_mousePoint.X = e.X;
                //_mousePoint.Y = e.Y;
                mousePoint = MousePosition;
            }
        }

        private void pLName_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && mousePoint != Point.Empty)
            {
                this.Top += MousePosition.Y - mousePoint.Y;
                this.Left += MousePosition.X - mousePoint.X;
                mousePoint = MousePosition;
            }
            //this.PointToClient(MousePosition).Y
        }

        private void pLName_MouseUp(object sender, MouseEventArgs e)
        {
            mousePoint = Point.Empty;
        }
        #endregion

        private void pBExit_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void pLName_Click(object sender, EventArgs e)
        {
            this.BringToFront();
        }

        private void nUSmoothData_ValueChanged(object sender, EventArgs e)
        {
            switch(Convert.ToInt32(nUSmoothData.Value))
            {
                case 0: MotionControl.kfQ = 0.0; MotionControl.kfR = 0.0; break;
                case 1: MotionControl.kfQ = 0.1; MotionControl.kfR = 0.1; break;
                case 2: MotionControl.kfQ = 0.1; MotionControl.kfR = 10; break;
                case 3: MotionControl.kfQ = 0.01; MotionControl.kfR = 50; break;
                case 4: MotionControl.kfQ = 10; MotionControl.kfR = 0.1; break;
                case 5: MotionControl.kfQ = 50; MotionControl.kfR = 0.01; break;
            }
        }

       
    }

    public class ShowItem
    {
        public Color Cl;
        public DashStyle Ds;
        public bool IsCheck;
        public string Monitor;
        public byte Item;
        public ShowQueue sq;
        public byte Mask;
        public ShowItem(bool isCheck, byte item, string monitor, Color cl, DashStyle ds, int queueSize, byte mask)
        {
            IsCheck = isCheck;
            Item = item;
            Monitor = monitor;
            Cl = cl;
            Ds = ds;
            sq = new ShowQueue(queueSize);
            Mask = mask;
        }
    }


    public class Node
    {
        private int value;
        private Node next;
        public Node(int v)
        {
            value = v;
            this.next = null;
        }

        public int Value
        {
            get { return this.value; }
            set { this.value = value; }
        }

        public Node Next
        {
            get { return next; }
            set { next = value; }
        }
    }

    public class ShowQueue
    {
        public int QCount;
        public Node front;//队列头
        public Node rear;//队列尾
        private int num;//队列元素个数

        private object ob = new object();

        //public bool flag1 = true;
        //public bool flag2 = true;


        public ShowQueue(int queueSize)  //构造函数
        {
            front = rear = null;
            num = 0;
            QCount = queueSize;
        }

        public int Count()
        {
            return this.num;
        }

        public void Clear()
        {
            front = rear = null;
            num = 0;
        }

        public bool IsEmpty()
        {
            return (front == rear && num == 0);
        }

        public void EnQ(int data)   //   追加   
        {
            lock (ob)
            {
                Node q = new Node(data);
                if (rear == null)               //第一个元素入列时
                {
                    front = rear = q;
                }
                else
                {
                    if (Count() == QCount)
                    {
                        //DeQ();
                        Node p = front;
                        //链头指向后移一位
                        front = front.Next;

                        //如果此时链表为空，则同步修正rear
                        if (front == null)
                        {
                            rear = null;
                        }
                        num--;//个数-1
                    }
                    //把新元素挂到链尾
                    rear.Next = q;
                    //修正rear指向为最后一个元素
                    rear = q;
                }
                //元素总数+1
                num++;
            }
        }

        public int DeQ()
        {
            lock (ob)
            {
                if (IsEmpty())
                {
                    return 0;
                }
                //取链首元素
                Node p = front;
                //链头指向后移一位
                front = front.Next;

                //如果此时链表为空，则同步修正rear
                if (front == null)
                {
                    rear = null;
                }
                num--;//个数-1
                return 1;
            }
           
        }

        //public float[] QMean()
        //{
            //while (!flag2) { };
            //flag1 = false;
            //if (rear != null)
            //{
            //    float[] sum = new float[6];
            //    Node mean = front;
            //    while (mean != null)
            //    {
            //        for (int i = 0; i < 6; i++)
            //        {
            //            //sum[i] += mean.Data[i];
            //        }
            //        mean = mean.Next;
            //    }
            //    //for (int i = 0; i < 6; i++)
            //    //{
            //    //    sum[i] /= Count;
            //    //}
            //    flag1 = true;
            //    return sum;
            //}
            //else
            //{
            //    flag1 = true;
            //    return null;
            //}
        //}

        public int GetValue(int index)
        {
            lock(ob)
            {
                Node current = front;
                int j = 0;
                while (j != index)
                {
                    current = current.Next;
                    j++;
                }
                return current.Value;
            }
        }

        public float GetAverageValue(int m, int n)
        {
            if (m > Count())
            {
                return 0.0f;
            }
            float sum = 0;
            lock (ob)
            {
                Node current = front;
                int j = 0;
                while (j != m)
                {
                    current = current.Next;
                    j++;
                }
                while (j != n + 1)
                {
                    try
                    {
                        sum += current.Value;
                        current = current.Next;
                        j++;
                    }
                    catch (System.Exception ex)
                    {
                        n = j + 1;
                        MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
                        break;
                    }
                   
                }
                return sum / (n - m);
            }
        }

        public float GetMaxValue(int m, int n)
        {
            if (m > Count())
            {
                return 0.0f;
            }
            lock (ob)
            {
                Node current = front;
                int j = 0;
                while (j != m)
                {
                    current = current.Next;
                    j++;
                }
                float max = 0;
                try
                {
                     max = current.Value;
                }
                catch (System.Exception ex)
                {
                    MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
                    return max;
                }
              
                while (j != n + 1)
                {
                    try
                    {
                        if (current.Value > max)
                        {
                            max = current.Value;
                        }
                        current = current.Next;
                        j++;
                    }
                    catch (System.Exception ex)
                    {
                        n = j;
                        MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
                        break;
                    }

                }
                return max;
            }
        }

        public float GetMinValue(int m, int n)
        {
            if (m > Count())
            {
                return 0.0f;
            }
            lock (ob)
            {
                Node current = front;
                int j = 0;
                while (j != m)
                {
                    current = current.Next;
                    j++;
                }
                float min = 0;
                try
                {
                     min = current.Value;
                }
                catch (System.Exception ex)
                {
                    MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
                    return 0;
                }
                while (j != n + 1)
                {
                    try
                    {
                        if (current.Value < min)
                        {
                            min = current.Value;
                        }
                        current = current.Next;
                        j++;
                    }
                    catch (System.Exception ex)
                    {
                        n = j;
                        MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
                        break;
                    }

                }
                return min;
            }
        }

        public void Print(ref int[] eachValue)
        {
            lock(ob)
            {
                //int[] eachValue = new int[Count()];
                Node current = front;
                int j = 0;
                //while (current != null)
                while (j != eachValue.Length)
                {
                    eachValue[j] = current.Value;
                    current = current.Next;
                    j++;
                }
            }
        }
    }
}
