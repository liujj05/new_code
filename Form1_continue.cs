﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;

namespace SerialportSample
{
    public partial class SerialportSampleForm : Form
    {
        // 文件保存：
        StreamWriter sw;
        int file_seq_num = 0;

        int start_inlier_num = 10;
        //==========================

        private SerialPort comm = new SerialPort();
        private StringBuilder builder = new StringBuilder();//避免在事件处理方法中反复的创建，定义到外面。
        private long received_count = 0;//接收计数
    
        //------------------------------- LiuJiaJun ------------------------------------------------
        // private StringBuilder builder_data = new StringBuilder(); // 弃用这种类型的，不适合做接收变量
        private byte[] laser_data = new byte[16];
        private byte[] recv_buff = new byte[64];
        private int laser_data_index = 0;
        private long batch_received_count = 0;
        // 发送与接收的互斥
        private bool recv_over = true;
        private bool recv_OK = true;
        // 关于定时器1
        private bool timer_ON = false;
        private byte[] laser_send_chars = { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01, 0x84, 0x0A };
        // 关于定时器2
        private double dmA = 0;
        private double dHeight = 0;

        // 实际数据采集流程
        // 0. 定义低点
        // 0.1 IL 300
        // private double thresh_low = 300;
        // private double thresh_high = 350;
        // 0.2 IL 600
        private double thresh_low = 210;
        private double thresh_high = 240;

        // 1. 连续三点
        private int continue3high = 0;
        private int continue3low = 0;
        // 2. 数据缓冲区
        private double[] height_data = new double[512];
        private int height_data_num = 0;
        // 3. 数据采集开始标志位
        private bool Sample_Start = false;
        // 4. 刀片数量编号，用来命名数据文件
        private int Knife_num = 0;
       


        //------------------------------------------------------------------------------------------
        private long send_count = 0;//发送计数

        private delegate void DelegateCallBackData(byte[] data);
        //Mouse check drawing
        private DelegateCallBackData delegateData = null;

        public SerialportSampleForm()
        {
            InitializeComponent();
        }

        //窗体初始化
        private void Form1_Load(object sender, EventArgs e)
        {
            // Liu - 清空，保险起见
            Array.Clear(laser_data, 0, laser_data.Length);

            string str = "0xEE";
            string str1 = "0x16";
            int i = Convert.ToInt32(str, 16);
            int ii = Convert.ToInt32(str1, 16);
            string temp = string.Format("{0:X}", i);
            string temp1 = string.Format("{0:X}", ii);
            //初始化下拉串口名称列表框
            string[] ports = SerialPort.GetPortNames();
            Array.Sort(ports);
            comboPortName.Items.AddRange(ports);
            comboPortName.SelectedIndex = comboPortName.Items.Count > 0 ? 0 : -1;
            comboBaudrate.SelectedIndex = comboBaudrate.Items.IndexOf("9600");

            //初始化SerialPort对象
            comm.NewLine = "\r\n";
            comm.RtsEnable = true;//根据实际情况吧。

            //添加事件注册
            comm.DataReceived += comm_DataReceived;

            delegateData += ReceivedData;
        }


        // 添加定时器1
        private void timer1_Tick(object sender, EventArgs e)
        {
            // 添加数据处理、解析部分
            //System.Diagnostics.Debug.WriteLine("###DEBUG### Timer1 - laser_data_index is {0}", laser_data_index);
            //System.Diagnostics.Debug.WriteLine("###DEBUG### Timer1 - batch_received_count is {0}", batch_received_count);
            if (batch_received_count == 7)
            {
                ReceivedData(laser_data);
            }

            else if (recv_OK == false)
            {
                System.Diagnostics.Debug.WriteLine("Timer1 -- receive not OK!");
                timer_ON = false;
            }
        }


        // 添加定时器2
        private void timer2_Tick(object sender, EventArgs e)
        {
            if (timer_ON)
            {
                // SetChartData(dmA);
            }
        }



        private string HexStringToString(string hs, Encoding encode)
        {
            string[] chars = hs.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            byte[] b = new byte[chars.Length];
            for (int i = 0; i < chars.Length; i++)
            {
                b[i] = Convert.ToByte(chars[i], 16);
            }
            return encode.GetString(b);
        }

        private void ReceivedData(byte[] data)
        {
            this.timer1.Stop();
            //================Liu 代码=========================
            //Step 1. 判断前三个是否为固定开头数据
            // byte[] batchdata = System.Text.Encoding.Default.GetBytes(data);
            if (data[0] != 0x01)
            {
                // 报错-进入纠错机制
                //SetChartData(-100.0);
                System.Diagnostics.Debug.WriteLine("###DEBUG### Data X");
                return;
            }
            if (data[1] != 0x03)
            {
                // 报错-进入纠错机制
                //SetChartData(-100.0);
                System.Diagnostics.Debug.WriteLine("###DEBUG### Data X");
                return;
            }
            if (data[2] != 0x02)
            {
                // 报错-进入纠错机制
                //SetChartData(-100.0);
                System.Diagnostics.Debug.WriteLine("###DEBUG### Data X");
                return;
            }

            //System.Diagnostics.Debug.WriteLine("###DEBUG### Data OK");

            // 文件头检验通过-解析数据
            UInt32 laser_data;
            laser_data = data[3];
            laser_data = laser_data << 8;
            laser_data = laser_data + data[4];

            // 毫安值：
            dmA = (laser_data * 20.0) / 10000.0;

            // 高度：
            // IL 300 比较特殊
            // 0 位置在300，远离最多到450，靠近可以到160
            // 整个这一段均匀分布在4-20mA
            // 所以，x = 4 对应 y = 450; x = 20 对应 y = 160
            // IL300
            //dHeight = (dmA - 4.0) * (160.0 - 450.0) / 16.0 + 450.0;
            // IL600
            dHeight = (dmA - 4.0) * (200.0 - 1000.0) / 16.0 + 1000.0;
            

            sw.WriteLine("{0}\t{1}", dHeight, dmA);

            
            batch_received_count = 0;
            laser_data_index = 0;
            comm.Write(laser_send_chars, 0, 8);
            this.timer1.Start();
            SetChartData(dHeight);

        }
        private int nRow = 1;
        private delegate void DelegateCChart(double data);
        private void SetChartData(double data)
        {
            try
            {
                if (chart1.InvokeRequired)
                {
                    DelegateCChart md = new DelegateCChart(SetChartData);
                    this.Invoke(md, new object[] { data });
                }
                else
                {
                    chart1.Series[0].Points.AddXY(nRow, data);
                    nRow++;
                }
                
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message.ToString());
            }
        }

        void comm_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {

            int n = comm.BytesToRead;//先记录下来，避免某种原因，人为的原因，操作几次之间时间长，缓存不一致
         
            received_count += n;//增加接收计数
            comm.Read(recv_buff, 0, n);//读取缓冲数据


            // Liu - Plan B 无脑收，只管是不是该清空了
            batch_received_count += n;
            for (int i=0; i<n; i++)
            {             
                laser_data[laser_data_index++] = recv_buff[i];
            }

            
        }

        private void buttonOpenClose_Click(object sender, EventArgs e)
        {
            //根据当前串口对象，来判断操作
            if (comm.IsOpen)
            {
                //打开时点击，则关闭串口
                comm.Close();
            }
            else
            {
                //关闭时点击，则设置好端口，波特率后打开
                comm.PortName = comboPortName.Text;
                comm.BaudRate = int.Parse(comboBaudrate.Text);
                try
                {
                    comm.Open();
                }
                catch(Exception ex)
                {
                    //捕获到异常信息，创建一个新的comm对象，之前的不能用了。
                    comm = new SerialPort();
                    //现实异常信息给客户。
                    MessageBox.Show(ex.Message);
                }
            }
            //设置按钮的状态
            buttonOpenClose.Text = comm.IsOpen ? "Close" : "Open";
            buttonSend.Enabled = comm.IsOpen;
        }

        //动态的修改获取文本框是否支持自动换行。
        private void checkBoxNewlineGet_CheckedChanged(object sender, EventArgs e)
        {
            txGet.WordWrap = checkBoxNewlineGet.Checked;
        }

        private void buttonSend_Click(object sender, EventArgs e)
        {
            //正式代码
            
            if (timer_ON)
            {
                timer_ON = false;
                this.timer1.Stop();

                sw.Flush();
                sw.Close();
            }
            else
            {
                // 新建文件

                sw = new StreamWriter("data_seq" + file_seq_num, false);
                file_seq_num++;
                comm.Write(laser_send_chars, 0, 8);
                this.timer1.Start();
                timer_ON = true;
            }
            
        }

        private void buttonReset_Click(object sender, EventArgs e)
        {
            //复位接受和发送的字节数计数器并更新界面。
            send_count = received_count = 0;
            labelGetCount.Text = "Get:0";
            labelSendCount.Text = "Send:0";
        }
    }
}
