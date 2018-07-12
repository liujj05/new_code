using System;
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
using System.Threading;

namespace _2018_7_10_T
{
    public partial class Form1 : Form
    {
        //=======================================================
        // 多线程代码，参考：https://www.cnblogs.com/wangsai/p/4113279.html
        private delegate void FlushClient(); // 代理
        // 读取更新数据的文件变量
        StreamReader Data_File = null;
        // 文件的基础名称
        string file_base_name = "knife";
        // 文件编号
        int file_num = 0;
        // 用于存储数据的list
        List <double> chart_data_height = new List<double>();
        List <double> chart_x_index = new List<double>();

        // 用于采集统计数据的list
        List <double> delta_height = new List<double>();
        // 用于保存标准数据的list
        List <double> Standard_height = new List<double>();
        int standard_data_num = 0;
        double average_db = 0;  // 平均值
        double stdeval_db = 0;  // 标准差
        //=======================================================

        private SerialPort comm = new SerialPort();
        private StringBuilder builder = new StringBuilder();//避免在事件处理方法中反复的创建，定义到外面。
        private long received_count = 0;//接收计数
        private long send_count = 0;//发送计数

        // ---------------------------------------- 算法变量初始化 ----------------------------------------
        
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
        private double thresh_high = 500;//240;

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

        // -----------------------------------------------------------------------------------------------


        private delegate void DelegateCallBackData(byte[] data); // Liu - 改成我需要的 byte[] 类型
        //Mouse check drawing
        private DelegateCallBackData delegateData = null;
        
        public Form1()
        {
            InitializeComponent();
        }
 
        private void Form1_Load(object sender, EventArgs e) 
        {
            // Liu - 清空，保险起见
            Array.Clear(laser_data, 0, laser_data.Length);

            // 载入标准刀具文件 - 这里假设这个文件肯定有
            Standard_height.Clear();
            StreamReader sr_standard_file = new StreamReader("StandardKnife", false);
            string str_data = "";
            while (true)
            {
                str_data = sr_standard_file.ReadLine();
                if (null == str_data)
                    break;
                else
                {
                    Standard_height.Add(System.Convert.ToDouble(str_data));
                    standard_data_num++;
                }
            }
            sr_standard_file.Close();


            //===============Liu - 多线程-初始化线程===========
            Thread t1 = new Thread(CrossThreadFlush);
            t1.IsBackground = true;
            t1.Start();
            //================================================

            //初始化下拉串口名称列表框
            string[] ports = SerialPort.GetPortNames();
            Array.Sort(ports);
            combo_PortName.Items.AddRange(ports);
            combo_PortName.SelectedIndex = combo_PortName.Items.Count > 0 ? 0 : -1;
            combo_Baudrate.SelectedIndex = combo_Baudrate.Items.IndexOf("9600");

            //添加事件注册
            comm.DataReceived += comm_DataReceived;
            delegateData += ReceivedData;

            // //判断串口当前状态，是开、还是关
            // if (comm.IsOpen)
            // {
            //    // comm.Close();//怎么和stop按钮联合使用?
            // }
            // comm.ReadTimeout = 32;//读取超时之前的毫秒数
            // try
            // {
            //     //comm.Open();//怎么和start按钮联合使用?
            // }
            // catch
            // {
            //     MessageBox.Show("没发现此串口或串口已经在使用");
            // }



            imageList1.Images.Add(Image.FromFile(@"C:\Users\Administrator\Desktop\2018-7-10-T\2018-7-10-T\PNG\Circle_Grey.png"));//读取灰色图标的路径,注意更换地址
            imageList1.Images.Add(Image.FromFile(@"C:\Users\Administrator\Desktop\2018-7-10-T\2018-7-10-T\PNG\Circle_Green.png"));//读取绿色图标的路径
            imageList1.Images.Add(Image.FromFile(@"C:\Users\Administrator\Desktop\2018-7-10-T\2018-7-10-T\PNG\Circle_Red.png"));//读取红色图标的路径
            this.pictureBox_LED.Image = imageList1.Images[0];
        }

        //============================多线程相关函数=======================================
        private void CrossThreadFlush()
        {
            while (true)
            {
                //将sleep和无限循环放在等待异步的外面
                // liu- 看起来最终会无限循环这里面的内容了
                Thread.Sleep(1000);

                // 判断文件是否存在
                if (!File.Exists(file_base_name+file_num))
                    continue;
                Data_File = null;
                // 判断文件是否被占用
                try
                {
                    Data_File = new StreamReader(file_base_name + file_num, false);
                }
                catch
                {
                    continue;
                }

                // 如果以上两步通过，说明文件存在，开始读取
                file_num++; // 文件编号自增，下次不读这次的了
                chart_data_height.Clear();
                chart_x_index.Clear();
                delta_height.Clear();
                string str = "";
                double x_index = 0;
                while (true)
                {
                    str = Data_File.ReadLine();
                    if (null == str)
                        break;
                    else
                    {
                        chart_data_height.Add(System.Convert.ToDouble(str));
                        // chart_x_index.Add(x_index); // 这回要画的不是绝对高度是相对误差量
                        x_index = x_index + 1;
                    }
                }
                Data_File.Close();
                // 计算均值
                int loop_max = System.Math.Min((int)(x_index), standard_data_num);
                for (int i = 0; i < loop_max; i++)
                {
                    delta_height.Add( chart_data_height[i] - Standard_height[i] );
                    chart_x_index.Add( (double)(i) )
                    average_db = average_db + delta_height[i];
                }
                average_db = average_db / (double)(loop_max);
                // 计算方差
                for (int i = 0; i < loop_max; i++)
                {
                    stdeval_db = stdeval_db + (delta_height[i] - average_db) * (delta_height[i] - average_db);
                }
                stdeval_db = stdeval_db / (double)(loop_max); // 这是方差
                // stdeval_db = System.Math.Sqrt(stdeval_db); // 这是标准差
                // 开始绘制
                ThreadFunction();
            }
        }
        private void ThreadFunction()
        {
            if (this.textBox1.InvokeRequired)//等待异步 
            {
                FlushClient fc = new FlushClient(ThreadFunction);
                this.Invoke(fc);//通过代理调用刷新方法 
            }
            else
            {
                // this.textBox1.Text = DateTime.Now.ToString();
                chart1.Series[0].Points.DataBindXY(chart_x_index, chart_data_height);
                textBox1.Text = average_db.ToString();
                textBox2.Text = stdeval_db.ToString();
            }
        }
        //================================================================================

        // 添加定时器1
        private void timer1_Tick(object sender, EventArgs e)
        {
            // 添加数据处理、解析部分
            //System.Diagnostics.Debug.WriteLine("###DEBUG### Timer1 - batch_received_count is {0}", batch_received_count);
            if (batch_received_count == 7)
            {
                
                //System.Diagnostics.Debug.WriteLine("###DEBUG### Timer1 - Recv_buff 6 is {0}", recv_buff[6]);
                ReceivedData(laser_data);
                
            }

            else if (recv_OK == false)
            {
                System.Diagnostics.Debug.WriteLine("Timer1 -- receive not OK!");
                timer_ON = false;
            }
        }


        private int temp = 0;
        private void buttonTest_Click(object sender, EventArgs e)
        {

            if (temp < 3)
            {
                pictureBox_LED.Image = imageList1.Images[temp++];
            }
            else
            {
                temp = 1;
                pictureBox_LED.Image = imageList1.Images[0];
            }
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
            dHeight = (dmA - 4.0) * (200.0 - 1000.0) / 16.0 + 800.0;
            // - 设置记录机制 -
            
            if (false == Sample_Start) // 如果采样没有开始
            {
                if (dHeight < thresh_low) // 进入连续接近三点的判断
                {
                    height_data[continue3low++] = dHeight;
                    if (continue3low == 3)
                    {
                        height_data_num = 3;
                        continue3low = 0;
                        Sample_Start = true;
                    }
                }
                else
                    continue3low = 0;
            }
            else // 采样开始
            {
                if (height_data_num < 512)
                    height_data[height_data_num++] = dHeight;
                else
                    height_data_num = 1000;

                if (dHeight > thresh_high) // 进入连续远离三点的判断
                {
                    continue3high++;
                    if (continue3high == 3)
                    {
                        if (height_data_num != 1000)
                            height_data_num = height_data_num - 3;
                        continue3high = 0;
                        // 保存数据到文件，注意，这时要停掉定时器，但是目前定时器操作部分有一些在函数外部，
                        // 所以这项工作留待后续统一做
                        StreamWriter sw = new StreamWriter("knife" + Knife_num, false);
                        for (int i=0; i<height_data_num; i++)
                        {
                            sw.WriteLine("{0}", height_data[i]);
                        }
                        Knife_num++;
                        sw.Flush();
                        sw.Close();

                        // 停止采样
                        Sample_Start = false;
                    }
                }
                else
                    continue3high = 0;
            }

            batch_received_count = 0;
            laser_data_index = 0;
            comm.Write(laser_send_chars, 0, 8);
            this.timer1.Start();
            // SetChartData(dHeight);
        }
        //图表的代码
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

        // --------------------------------------- 接收串口信息 ----------------------------------------
        void comm_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            


            int n = comm.BytesToRead;//
            
            received_count += n;//增加接收计数
            comm.Read(recv_buff, 0, n);//读取缓冲数据


            // Liu - Plan B 无脑收，只管是不是该清空了
            batch_received_count += n;
            for (int i=0; i<n; i++)
            {
                //System.Diagnostics.Debug.WriteLine("###DEBUG### laser_data_index is {0}", laser_data_index);
                
                
                laser_data[laser_data_index++] = recv_buff[i];

            }

            // 测试代码
            /*
            System.Diagnostics.Debug.WriteLine("###DEBUG### - comm_DataReceived");
            System.Diagnostics.Debug.WriteLine("###DEBUG### - comm_DataReceived recv_num is {0}", n);
            for (int i=0; i<7; i++)
            {
                System.Diagnostics.Debug.WriteLine("###DEBUG### - comm_DataReceived {0} is {1}", i, recv_buff[i]);
            }
            */
        }
        // --------------------------------------------------------------------------------------------------
        
       
        private void Start_Click(object sender, EventArgs e)
        {
            Text = "程序启动，串口打开";
            //-------------------------Liu 打开串口前要对参数进行配置 --------------------------
            comm.PortName = combo_PortName.Text;
            comm.BaudRate = int.Parse(combo_Baudrate.Text);
            
            // ------------------------Liu 打开失败要停止流程防止错误 --------------------------
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
                return;
            }

            MessageBox.Show("程序启动，串口打开");

            // 后续代码为启动代码
            if (timer_ON)
            {
                timer_ON = false;
                this.timer1.Stop();
            }
            else
            {
                comm.Write(laser_send_chars, 0, 8);
                this.timer1.Start();
                timer_ON = true;
            }
        }
        private void buttonStop_Click(object sender, EventArgs e)
        {
            Text = "停止运行，串口关闭";
            this.timer1.Stop();
            comm.Close();//串口关闭
            MessageBox.Show("程序停止，串口关闭");
        }
        
        
            
            //显示指示灯图片的代码，只能直接打开指定的位置
            /*OpenFileDialog file = new OpenFileDialog();
            file.InitialDirectory = ".";
            file.Filter = "所有文件(*.*)|*.*";
            file.ShowDialog();
            if (file.FileName != string.Empty)
            {
                try
                {
                    pathname = file.FileName;   //获得文件的绝对路径
                    this.pictureBox指示灯.Load(pathname);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            } */
    }      
}
       

   
