using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;

namespace ICDIBasic
{
    public partial class TestRun : Form
    {
        public static int m_iWaveMode = 0;
        public static int m_iWaveChannel = 0;

        public static bool EnableRun = false;
        public static bool ChangeOK = false;

        public static float m_fFrequency = 0.5f;
        public static float m_fAmplitude = 0.0f;
        public static float m_fBias = 0.0f;
        public static float StepLength = 0.1f;

       float m_fFrequency2 = 0.5f;
       float m_fAmplitude2 = 0.0f;
       float m_fBias2 = 0.0f;
       


        PCan pc;
        Thread threadDirectMove;
        public bool NormalExit = false;


        public static TestRun pCurrentWin = null;//句柄
        public TestRun()
        {
            InitializeComponent();
            pc = new PCan();
            InitialControls();

        }

        public static TestRun GetInstance()
        {
            if (pCurrentWin == null)
            {
                pCurrentWin = new TestRun();
            }
            return pCurrentWin;
        }

        private void TestRun_FormClosed(object sender, FormClosedEventArgs e)
        {
            pCurrentWin = null;
        }

        private void InitialControls()
        {
            //初始化控制模式
            switch (Configuration.MemoryControlTable[Configuration.TAG_WORK_MODE])
            {
                case 0: cBControlMode.SelectedIndex = 0; break;
                case 1: cBControlMode.SelectedIndex = 1; break;
                case 2: cBControlMode.SelectedIndex = 2; break;
                case 3: cBControlMode.SelectedIndex = 3; break;
            }

            InitialAutomaticControl();

            pLEnable.Enabled = false;
        }

        private void cBWaveform_SelectedIndexChanged(object sender, EventArgs e)
        {
            clearValue();
            setInitalValue();
            switch (cBWaveForm.Text)
            {
                case "衡值": m_iWaveMode = MotionControl.WAVE_MODE_DC; tBFrequency.Enabled = false; tBAmplitude.Enabled = false; break;
                case "方波": m_iWaveMode = MotionControl.WAVE_MODE_SQUARE; tBFrequency.Enabled = true; tBAmplitude.Enabled = true; break;
                case "三角波": m_iWaveMode = MotionControl.WAVE_MODE_TRIANGLE; tBFrequency.Enabled = true; tBAmplitude.Enabled = true; break;
                case "正弦波": m_iWaveMode = MotionControl.WAVE_MODE_SINE; tBFrequency.Enabled = true; tBAmplitude.Enabled = true; break;
            }
            IniFile.WritePrivateProfileString("TestRun", "WaveForm", cBWaveForm.SelectedIndex.ToString(), IniFile.StrProPath);
        }

        private void cBControlMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            ChangeOK = false;
            Thread.Sleep(5);
            switch (cBControlMode.Text)
            {
                case "开环占空比": m_iWaveChannel = MotionControl.WAVE_CONNECT_PWM; pc.WriteOneWord(Configuration.TAG_WORK_MODE, Configuration.MODE_OPEN, PCan.currentID); lLUnit.Text = "Unit: %"; break;
                case "电流控制": m_iWaveChannel = MotionControl.WAVE_CONNECT_CUR; pc.WriteOneWord(Configuration.TAG_WORK_MODE, Configuration.MODE_CURRENT, PCan.currentID); lLUnit.Text = "Unit: mA"; break;
                case "速度控制": m_iWaveChannel = MotionControl.WAVE_CONNECT_SPD; pc.WriteOneWord(Configuration.TAG_WORK_MODE, Configuration.MODE_SPEED, PCan.currentID); lLUnit.Text = "Unit: rpm"; break;
                case "位置控制": m_iWaveChannel = MotionControl.WAVE_CONNECT_POS; pc.WriteOneWord(Configuration.TAG_WORK_MODE, Configuration.MODE_POSITION, PCan.currentID); lLUnit.Text = "Unit: °"; break;
            }
            //取消cBControlMode的输入焦点
            pBMode.Focus();
            //
            if (Convert.ToInt32(pBMode.Tag) == 1)
            {
                //MamuallyControl();
            }
            else
            {
                clearValue();
            }

            IniFile.WritePrivateProfileString("TestRun", "ControlMode", cBControlMode.SelectedIndex.ToString(), IniFile.StrProPath);
        }

        private void tBFrequency_TextChanged(object sender, EventArgs e)
        {
            try
            {
                m_fFrequency2 = Convert.ToSingle(tBFrequency.Text);
            }
            catch (System.Exception ex)
            {
                MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
                tBFrequency.Text = "0.2";
                MessageBox.Show("请输入合法的数值！");
            }
            IniFile.WritePrivateProfileString("WaveForm" + cBWaveForm.SelectedIndex, "WaveFrequency", m_fFrequency2.ToString(), IniFile.StrProPath);
        }

        private void tBAmplitude_TextChanged(object sender, EventArgs e)
        {
            try
            {
                m_fAmplitude2 = Convert.ToSingle(tBAmplitude.Text);
            }
            catch (System.Exception ex)
            {
                MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
                tBAmplitude.Text = "0";
                MessageBox.Show("请输入合法的数值！");
            }
            IniFile.WritePrivateProfileString("WaveForm" + cBWaveForm.SelectedIndex, "WaveAmplitude", m_fAmplitude2.ToString(), IniFile.StrProPath);
        }

        private void tBBias_TextChanged(object sender, EventArgs e)
        {
            try
            {
                m_fBias2 = Convert.ToSingle(tBBias.Text);
            }
            catch (System.Exception ex)
            {
                tBBias.Text = "0";
                MessageBox.Show("请输入合法的数值！");
                MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
            }
            IniFile.WritePrivateProfileString("WaveForm" + cBWaveForm.SelectedIndex, "WaveBias", m_fBias2.ToString(), IniFile.StrProPath);
        }

        private void btnEnable_Click(object sender, EventArgs e)
        {
            //cBCurrentRatio.Text = IniFile.ContentValue("plRange", "Current", IniFile.StrProPath);
            //IniFile.WritePrivateProfileString("OscilloScope", "PIDGroup", type, IniFile.StrProPath);
            if (btnEnable.Text == "使能开启")
            {
                EnableRun = true;
                btnEnable.Text = "使能关闭";
                btnEnable.BackColor = Color.IndianRed;
                pLEnable.Enabled = true;
                //默认选择项
                switch (Configuration.MemoryControlTable[Configuration.TAG_WORK_MODE])
                {
                    case 0: cBControlMode.SelectedIndex = 0; break;
                    case 1: cBControlMode.SelectedIndex = 1; break;
                    case 2: cBControlMode.SelectedIndex = 2; break;
                    case 3: cBControlMode.SelectedIndex = 3; break;
                }
            }
            else
            {
                //将运动控制变量清零
                EnableRun = false;
                pc.WriteOneWord(Configuration.TAG_OPEN_PWM, 0, PCan.currentID);
                pc.WriteTwoWords(Configuration.TAG_CURRENT_L, 0, PCan.currentID);
                pc.WriteTwoWords(Configuration.TAG_SPEED_L, 0, PCan.currentID);
                pc.WriteTwoWords(Configuration.TAG_POSITION_L, 0, PCan.currentID);

                btnEnable.Text = "使能开启";
                btnEnable.BackColor = Color.Green;
                pLEnable.Enabled = false;
            }
        }

        private void btnConfirm_Click(object sender, EventArgs e)
        {
            //更新控制变量
            m_fFrequency = m_fFrequency2;
            m_fAmplitude = m_fAmplitude2;
            m_fBias = m_fBias2;

            ChangeOK = true;
        }

        private void tBFrequency_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                m_fFrequency = m_fFrequency2;
        }

        private void tBAmplitude_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                m_fAmplitude = m_fAmplitude2;
        }

        private void tBBias_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                m_fBias = m_fBias2; 
        }

        private void btnClearAll_Click(object sender, EventArgs e)
        {
            clearValue(); 
        }

        private void pBMode_Click(object sender, EventArgs e)
        {
            if (Convert.ToInt32(pBMode.Tag) == 1)
            {
                MamuallyControl();
            } 
            else
            {
                InitialAutomaticControl();
            }
        }

        private void InitialAutomaticControl()
        {
            pBMode.Tag = 1;
            pBMode.Image = Image.FromFile(Application.StartupPath + "\\resource\\A.jpg");
            gBManually.Enabled = false;
            gBWaveFormProperty.Enabled = true;

            gBManually.BackColor = Color.White;
            gBWaveFormProperty.BackColor = Color.SlateGray;

            cBWaveForm.SelectedIndex = Convert.ToInt32(IniFile.ContentValue("TestRun", "WaveForm", IniFile.StrProPath));
            lLUnit.Text = "Unit: %";
            setInitalValue();
        }

        void setInitalValue()
        {
            string strWaveFrequency = IniFile.ContentValue("WaveForm" + cBWaveForm.SelectedIndex, "WaveFrequency", IniFile.StrProPath);
            string strWaveAmplitude = IniFile.ContentValue("WaveForm" + cBWaveForm.SelectedIndex, "WaveAmplitude", IniFile.StrProPath);
            string strWaveBias = IniFile.ContentValue("WaveForm" + cBWaveForm.SelectedIndex, "WaveBias", IniFile.StrProPath);

            m_fFrequency = Convert.ToSingle(strWaveFrequency);
            m_fAmplitude = Convert.ToSingle(strWaveAmplitude);
            m_fBias = Convert.ToSingle(strWaveBias);

            tBFrequency.Text = strWaveFrequency;
            tBAmplitude.Text = strWaveAmplitude;
            tBBias.Text = strWaveBias;
        }

        public void clearValue()
        {
            ChangeOK = false;
            Thread.Sleep(5);

            pc.WriteOneWord(Configuration.TAG_WORK_MODE, Configuration.MODE_SPEED, PCan.currentID);
            pc.WriteTwoWords(Configuration.TAG_SPEED_L, 0, PCan.currentID);
         
            Thread.Sleep(10);
            switch (cBControlMode.Text)
            {
                case "开环占空比": m_iWaveChannel = MotionControl.WAVE_CONNECT_PWM; pc.WriteOneWord(Configuration.TAG_WORK_MODE, Configuration.MODE_OPEN, PCan.currentID); lLUnit.Text = "Unit: %"; break;
                case "电流控制": m_iWaveChannel = MotionControl.WAVE_CONNECT_CUR; pc.WriteOneWord(Configuration.TAG_WORK_MODE, Configuration.MODE_CURRENT, PCan.currentID); lLUnit.Text = "Unit: mA"; break;
                case "速度控制": m_iWaveChannel = MotionControl.WAVE_CONNECT_SPD; pc.WriteOneWord(Configuration.TAG_WORK_MODE, Configuration.MODE_SPEED, PCan.currentID); lLUnit.Text = "Unit: rpm"; break;
                case "位置控制": m_iWaveChannel = MotionControl.WAVE_CONNECT_POS; pc.WriteOneWord(Configuration.TAG_WORK_MODE, Configuration.MODE_POSITION, PCan.currentID); lLUnit.Text = "Unit: °"; break;
            }

            m_fFrequency = 0.0f;
            m_fAmplitude = 0.0f;
            m_fBias = 0.0f;
        }

        private void MamuallyControl()
        {
            pBMode.Tag = 2;
            pBMode.Image = Image.FromFile(Application.StartupPath + "\\resource\\M.jpg");
            gBManually.Enabled = true;
            gBWaveFormProperty.Enabled = false;
            gBManually.BackColor = Color.SlateGray;
            gBWaveFormProperty.BackColor = Color.White;

            pc.WriteOneWord(Configuration.SCP_MASK, OscilloScope.Mask, PCan.currentID);       //向下位机请求数据
        }

        private void pBExit_Click(object sender, EventArgs e)
        {
            //将运动控制变量清零
            EnableRun = false;
            clearValue();

            btnEnable.Text = "使能开启";
            btnEnable.BackColor = Color.Green;
            pLEnable.Enabled = false;

            this.Close();
        }

        private void pLName_Click(object sender, EventArgs e)
        {
            this.BringToFront();
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

        private void btnReverse_MouseDown(object sender, MouseEventArgs e)
        {
            if (threadDirectMove != null)
            {
                if (threadDirectMove.IsAlive)
                {
                    return;
                }
            }
            else
            {
                Button btn = sender as Button;
                string str = btn.Name;
                NormalExit = false;
                threadDirectMove = new Thread(new ParameterizedThreadStart(JogThread));
                threadDirectMove.Priority = ThreadPriority.Highest;
                threadDirectMove.Start(str);
            }
        }

        private void btnReverse_MouseUp(object sender, MouseEventArgs e)
        {
            NormalExit = true;
        }

        private void JogThread(object str)
        {
            int AcTime = Convert.ToInt32(StepLength * 10);
            int count = 0;
            bool DecExit = false;
            
            while (!DecExit)
            {
                if (!NormalExit)
                {
                    count++;
                    if (count > AcTime)
                    {
                        count = AcTime;
                    }
                }
                else
                {
                    count--;
                    if (count == 0)
                    {
                        DecExit = true;
                        break;
                    }
                }
                byte bt = Configuration.TAG_OPEN_PWM;
                switch(m_iWaveChannel)
                {
                    case MotionControl.WAVE_CONNECT_PWM: pc.ReadWords(Configuration.TAG_OPEN_PWM, 1, PCan.currentID); break;
                    case MotionControl.WAVE_CONNECT_CUR: bt = Configuration.SCP_MEACUR_L; break;
                    case MotionControl.WAVE_CONNECT_SPD: bt = Configuration.SCP_MEASPD_L; break;
                    case MotionControl.WAVE_CONNECT_POS: bt = Configuration.SCP_MEAPOS_L; break;
                }

                //以固定频率控制运动 10ms
                threadDirectMove.Join(10);

                int value = 0;
                if (bt != Configuration.TAG_OPEN_PWM)
                {
                    byte[] value1 = BitConverter.GetBytes(Configuration.MemoryControlTable[bt]);
                    byte[] value2 = BitConverter.GetBytes(Configuration.MemoryControlTable[bt + 1]);
                    value = BitConverter.ToInt32(new byte[] { value1[0], value1[1], value2[0], value2[1] }, 0);
                }
                else
                {
                    value = Configuration.MemoryControlTable[Configuration.TAG_OPEN_PWM];
                }

                if (str.ToString() == "btnReverse")
                {
                    value -= Convert.ToInt32(count * StepLength / AcTime);
                }
                else
                {
                    value += Convert.ToInt32(count * StepLength / AcTime);
                }

                switch (m_iWaveChannel)
                {
                    case MotionControl.WAVE_CONNECT_PWM: pc.WriteTwoWords(Configuration.TAG_OPEN_PWM, value, PCan.currentID); this.Invoke((EventHandler)(delegate{ showManuallyDta(value, 1); })); break;
                    case MotionControl.WAVE_CONNECT_CUR: pc.WriteTwoWords(Configuration.TAG_CURRENT_L, value, PCan.currentID); this.Invoke((EventHandler)(delegate{ showManuallyDta(value, 2); })); break;
                    case MotionControl.WAVE_CONNECT_SPD: pc.WriteTwoWords(Configuration.TAG_SPEED_L, value, PCan.currentID); this.Invoke((EventHandler)(delegate{ showManuallyDta(value, 3); })); break;
                    case MotionControl.WAVE_CONNECT_POS: pc.WriteTwoWords(Configuration.TAG_POSITION_L, value, PCan.currentID); this.Invoke((EventHandler)(delegate{ showManuallyDta(value, 4); })); break;
                }
              
            }

            this.Invoke((EventHandler)(delegate
            { MainForm.GetInstance().sBFeedbackShow("手动线程退出！", 1); }));
            threadDirectMove.Abort();
        }

        private void showManuallyDta(int value, int type)
        {
            double mData = 0.0f;
            switch (type)
            {
                case 1: mData = value; break;
                case 2: mData = value; break;
                case 3: mData = value * 60.0 / 65536; break;
                case 4: mData = value * 60.0 / 65536; break;
            }
            tBCur.Text = value.ToString(); tBManual.Value = Convert.ToInt32(mData);
        }

        private void tBStep_TextChanged(object sender, EventArgs e)
        {
            try
            {
                StepLength = Convert.ToSingle(tBStep.Text);
            }
            catch (System.Exception ex)
            {
                tBStep.Text = "0";
                MessageBox.Show("请输入合法的数值！");
                MainForm.GetInstance().sBFeedbackShow(ex.Message, 1);
            }
        }















    }
}
