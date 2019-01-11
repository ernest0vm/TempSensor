using System;
using System.Data.SqlClient;
using System.Diagnostics;
using Windows.Devices.Spi;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using System.Text;
using Windows.UI;

namespace TempSensor
{
    /// <summary>
    /// Página vacía que se puede usar de forma independiente o a la que se puede navegar dentro de un objeto Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        enum ADCChip
        {
            mcp3208  // 8 channel 12 bit
        }

        /*RaspBerry Pi3  Parameters*/
        private const string SPI_CONTROLLER_NAME = "SPI0";  /* For Raspberry Pi 2, use SPI0                             */
        private const Int32 SPI_CHIP_SELECT_LINE = 0;       /* Line 0 maps to physical pin number 24 on the Rpi2        */

        byte[] readBuffer = null;                           /* this is defined to hold the output data*/
        byte[] writeBuffer = null;                          /* we will hold the command to send to the chipbuild this in the constructor for the chip we are using */

        private SpiDevice SpiDisplay;
        private SqlConnection connection;
        
        // create a timer
        private DispatcherTimer timer;
        private DispatcherTimer timerData;
        int res;

        ADCChip whichADCChip = ADCChip.mcp3208;

        public MainPage()
        {

            this.InitializeComponent();  //se inicializa el contador 
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)  // intervalo de lectura  
            };
            timer.Tick += Timer_Tick;
            timer.Start();

            whichADCChip = ADCChip.mcp3208;
            switch (whichADCChip)
            {

                case ADCChip.mcp3208:
                    {
                        /* mcp3208 is 12 bits output */
                        // To line everything up for ease of reading back (on byte boundary) we 
                        // will pad the command start bit with 5 leading "0" bits

                        // Write 0000 0SGD DDxx xxxx xxxx xxxx
                        // Read  ???? ???? ???N BA98 7654 3210
                        // S = start bit
                        // G = Single / Differential
                        // D = Chanel data 
                        // ? = undefined, ignore
                        // N = 0 "Null bit"
                        // B-0 = 12 data bits


                        // 0000 0110 = 5 pad bits, start bit, single ended, channel bit 2
                        // 0000 0000 = channel bit 1, channel bit 0, 6 clocking bits
                        // 0000 0000 = 8 clocking bits
                        readBuffer = new byte[3] { 0x00, 0x00, 0x00 };
                        writeBuffer = new byte[3] { 0x06, 0x00, 0x00 };
                    }
                    break;
            }
#if ARM
            InitSPI();
            GpioStatus.Text = "Temperature sensor initialized";
            btnTest.Visibility = Visibility.Collapsed;
#endif
            ConnectDB();
            //CreateDB();
            //CreateTable();

            timerData = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)  // intervalo de lectura  
            };
            timerData.Tick += TimerData_Tick;
            timerData.Start();
        }

        private async void InitSPI()
        {
            try
            {
                GpioStatus.Text = "Waiting for GPIO to be initialized";
                var settings = new SpiConnectionSettings(SPI_CHIP_SELECT_LINE);
                settings.ClockFrequency = 500000; //10000000;
                settings.Mode = SpiMode.Mode0; //Mode3;
                var controller = await SpiController.GetDefaultAsync();
                SpiDisplay = controller.GetDevice(settings);
            }

            /* If initialization fails, display the exception and stop running */
            catch (Exception ex)
            {
                GpioStatus.Text= $"SPI Initialization Failed: {ex}";
            }
        }

        private void ConnectDB()
        {
            try
            {
            dbStatus.Text = "Database Status: ";
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
                
#if ARM
                builder.DataSource = "xxxx.database.windows.net";   // update me
                builder.UserID = "xxxx";              // update me
                builder.Password = "xxxx";      // update me
                builder.InitialCatalog = "TempSensor";
#else
                builder.ConnectionString = "Server=localhost;Database=master;Trusted_Connection=True;";
#endif

                // Connect to SQL
                Debug.WriteLine("Connecting to SQL Server ... ");
                connection = new SqlConnection(builder.ConnectionString);
                
                    connection.Open();
                    Debug.WriteLine("Done.");
                    dbPin.Fill = new SolidColorBrush(Colors.Green);
                
            }
            catch(SqlException ex)
            {
                Debug.WriteLine("Error connecting database: " + ex);
                dbPin.Fill = new SolidColorBrush(Colors.Red);
            }
        }

        private void CreateDB()
        {
            try
            {
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    Debug.WriteLine("Dropping and creating database 'TempSensor' ... ");
                    String sql = "DROP DATABASE IF EXISTS [TempSensor]; CREATE DATABASE [TempSensor]";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.ExecuteNonQuery();
                        Debug.WriteLine("Done.");
                    }

                }
            }catch(SqlException ex)
            {
                Debug.WriteLine($"Error creating database: {ex}");
                dbPin.Fill = new SolidColorBrush(Colors.Orange);
            }
        }

        private void CreateTable()
        {
            try
            {
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    // Create a Table and insert some sample data
                    Debug.WriteLine("Creating table");
                    StringBuilder sb = new StringBuilder();
                    sb.Append("USE TempSensor; ");
                    sb.Append("CREATE TABLE Temperatura ( ");
                    sb.Append(" Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY, ");
                    sb.Append(" data NVARCHAR(50), ");
                    sb.Append(" date NVARCHAR(50), ");
                    sb.Append(" time NVARCHAR(50) ");
                    sb.Append("); ");

                    string sql = sb.ToString();
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.ExecuteNonQuery();
                        Debug.WriteLine("Done.");
                    }
                }
            }
            catch (SqlException ex)
            {
                Debug.WriteLine($"Error creating table: {ex}");
                dbPin.Fill = new SolidColorBrush(Colors.Orange);
            }
        }

        private void Timer_Tick(object sender, object e)
        {
            fecha.Text = DateTime.Now.ToString("D");
            hora.Text = DateTime.Now.ToString("T"); // Sirve para poner fecha y hora del sistema  
#if ARM
            DisplayTextBoxContents();
#endif
        }

        private void TimerData_Tick(object sender, object e)
        {

            if (connection.State == System.Data.ConnectionState.Open)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("INSERT INTO Temperatura (data, date, time) VALUES ");
                sb.Append($"(N'{valortxt.Text}', N'{fecha.Text}', N'{hora.Text}'); ");

                string sql = sb.ToString();
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.ExecuteNonQuery();
                    Console.WriteLine("Done.");
                }
            }
        }
        public void DisplayTextBoxContents()
        {
            SpiDisplay.TransferFullDuplex(writeBuffer, readBuffer);
            res = convertToInt(readBuffer);


            Double volt = (4096 / 4096.0) * Convert.ToDouble(res);          // Se realiza la conversión de lo que entrega el ADC a grados Centigrados 
            Double temp = (volt * 0.1) - 40;
            textPlaceHolder.Text = $"ADC data: {res.ToString()}";
            //GpioStatus.Text = $"Volts: {volt}";

            if (tSwitch.IsOn == true)
            {
                temp = (1.8 * temp) + 32;
                valortxt.Text = temp.ToString("0.0") + " °F";                        // Valor entregado en grados Farenheit
            }
            else
            {
                valortxt.Text = temp.ToString("0.0") + " °C";                        // Valor entregado en grados centigrados  
            }

           

            }
        public int convertToInt(byte[] data)
        {
            int result = 0;
            switch (whichADCChip)
            {

                case ADCChip.mcp3208:
                    {
                        /* mcp3208 is 12 bits output */
                        result = data[1] & 0x0F;
                        result <<= 8;
                        result += data[2];
                    }
                    break;
            }

            return result;
        }

        private void test_click(object sender, RoutedEventArgs e)
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("INSERT INTO Temperatura (data, date, time) VALUES ");
                sb.Append($"(N'{valortxt.Text}', N'{fecha.Text}', N'{hora.Text}'); ");

                string sql = sb.ToString();
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.ExecuteNonQuery();
                    Console.WriteLine("Done.");
                }
            }
        }
    }
}
