using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO
using System.IO.Ports;
using System.Text.RegularExpressions;

namespace _2018_7_10_T
{
    public partial class Form1 : Form
    {
        private SerialPort comm = new SerialPort();
        private StringBuilder builder = new StringBuilder();//避免在事件处理方法中反复的创建，定义到外面。
        private long received_count = 0;//接收计数
        private long send_count = 0;//发送计数

        private delegate void DelegateCallBackData(string data);
        //Mouse check drawing
        private DelegateCallBackData delegateData = null;
        
        public Form1()
        {
            InitializeComponent();
        }
 
       private void Form1_Load(object sender, EventArgs e) 
      {

          //初始化下拉串口名称列表框
          string[] ports = SerialPort.GetPortNames();
          Array.Sort(ports);
          combo端口.Items.AddRange(ports);
          combo端口.SelectedIndex = combo端口.Items.Count > 0 ? 0 : -1;
          combo波特率.SelectedIndex = combo波特率.Items.IndexOf("9600");

          //添加事件注册
          //comm.DataReceived += comm_DataReceived;
          delegateData += ReceivedData;

            //判断串口当前状态，是开、还是关
            if (comm.IsOpen)
            {
               // comm.Close();//怎么和stop按钮联合使用?
            }
            comm.ReadTimeout = 32;//读取超时之前的毫秒数
            try
            {
                //comm.Open();//怎么和start按钮联合使用?
             }
            catch
            {
                MessageBox.Show("没发现此串口或串口已经在使用");
            }



            imageList1.Images.Add(Image.FromFile(@"C:\Users\Administrator\Desktop\2018-7-10-T\2018-7-10-T\PNG\Circle_Grey.png"));//读取灰色图标的路径,注意更换地址
            imageList1.Images.Add(Image.FromFile(@"C:\Users\Administrator\Desktop\2018-7-10-T\2018-7-10-T\PNG\Circle_Green.png"));//读取绿色图标的路径
            imageList1.Images.Add(Image.FromFile(@"C:\Users\Administrator\Desktop\2018-7-10-T\2018-7-10-T\PNG\Circle_Red.png"));//读取红色图标的路径
            this.pictureBox指示灯.Image = imageList1.Images[0];
       }
       private int temp = 0;
       private void buttonTest_Click(object sender, EventArgs e)
       {

           if (temp < 3)
           {
               pictureBox指示灯.Image = imageList1.Images[temp++];
           }
           else
           {
               temp = 1;
               pictureBox指示灯.Image = imageList1.Images[0];
           }
       }


       private void ReceivedData(string data)
       {
           string[] sArray;
           //接收数据是否存在01 03 02
           if (data.Contains("01 03 02"))
           {
               //得到01 03 02以外字串
               sArray = data.Split(new string[] { "01 03 02" }, StringSplitOptions.RemoveEmptyEntries);
               //如果获取字串数据大于0 说明得到01 03 02以外字串成功
               if (sArray.Length > 0)
               {
                   //截取4位有效字串，因为接收数据两位之间包含一个空格，因此设置6
                   sArray[0] = sArray[0].Remove(6);
                   //去掉字串中的空格
                   string temp = sArray[0].Replace(" ", "");

                   double nValue = 0;
                   //数据解析
                   for (int i = 0; i < temp.Length; i++)
                   {
                       string a = temp[i].ToString();
                       int nTemp = Convert.ToInt32(a, 16);
                       nValue += nTemp * Math.Pow(16, temp.Length - i - 1);
                   }
                   //毫安算法
                   double dMA = (nValue * 20) / 10000;
                   //高度算法
                   double dHigh = 18.0645 * dMA - 220.1225;

                   SetChartData(dHigh);
               }
           }
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
        
       
       private void Start_Click(object sender, EventArgs e)
       {
           Text = "程序启动，串口打开";
           comm.Open();//串口打开
           MessageBox.Show("程序启动，串口打开");
       }
        private void buttonStop_Click(object sender, EventArgs e)
        {
            Text = "停止运行，串口关闭";
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
       

   
