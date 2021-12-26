using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AForge.Video;
using AForge.Video.DirectShow;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace AForge.WindowsForms
{
    delegate void FormUpdateDelegate();

    public partial class MainForm : Form
    {
        /// <summary>
        /// Класс, реализующий всю логику работы
        /// </summary>
        private Controller controller = null;

        /// <summary>
        /// Событие для синхронизации таймера
        /// </summary>
        private AutoResetEvent evnt = new AutoResetEvent(false);
                
        /// <summary>
        /// Список устройств для снятия видео (веб-камер)
        /// </summary>
        private FilterInfoCollection videoDevicesList;
        
        /// <summary>
        /// Выбранное устройство для видео
        /// </summary>
        private IVideoSource videoSource;
        
        /// <summary>
        /// Таймер для измерения производительности (времени на обработку кадра)
        /// </summary>
        private Stopwatch sw = new Stopwatch();
        
        /// <summary>
        /// Таймер для обновления объектов интерфейса
        /// </summary>
        System.Threading.Timer updateTmr;

        /// <summary>
        /// Функция обновления формы, тут же происходит анализ текущего этапа, и при необходимости переключение на следующий
        /// Вызывается автоматически - это плохо, надо по делегатам вообще-то
        /// </summary>
        private void UpdateFormFields()
        {
            //  Проверяем, вызвана ли функция из потока главной формы. Если нет - вызов через Invoke
            //  для синхронизации, и выход
            if (status.InvokeRequired)
            {
                this.Invoke(new FormUpdateDelegate(UpdateFormFields));
                return;
            }

            sw.Stop();
            //ticksLabel.Text = "Тики : " + sw.Elapsed.ToString();
            originalImageBox.Image = controller.GetOriginalImage();
            processedImgBox.Image = controller.GetProcessedImage();
        }

        /// <summary>
        /// Обёртка для обновления формы - перерисовки картинок, изменения состояния и прочего
        /// </summary>
        /// <param name="StateInfo"></param>
        public void Tick(object StateInfo)
        {
            UpdateFormFields();
            return;
        }

        public MainForm(Dictionary<string, Func<int[], BaseNetwork>> networksFabric)
        {
            InitializeComponent();
            // Список камер получаем
            videoDevicesList = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            foreach (FilterInfo videoDevice in videoDevicesList)
            {
                cmbVideoSource.Items.Add(videoDevice.Name);
            }
            if (cmbVideoSource.Items.Count > 0)
            {
                cmbVideoSource.SelectedIndex = 0;
            }
            else
            {
                MessageBox.Show("А нет у вас камеры!", "Ошибочка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            controller = new Controller(new FormUpdateDelegate(UpdateFormFields));
            //            updateTmr = new System.Threading.Timer(Tick, evnt, 500, 100);
            this.networksFabric = networksFabric;
            netTypeBox.Items.AddRange(this.networksFabric.Keys.Select(s => (object)s).ToArray());
            netTypeBox.SelectedIndex = 0;
            //generator.FigureCount = (int)classCounter.Value;
            button3_Click(this, null);
        }

        private void video_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            //  Время засекаем
            sw.Restart();

            //  Отправляем изображение на обработку, и выводим оригинал (с раскраской) и разрезанные изображения
            if(controller.Ready)
                
                #pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                controller.ProcessImage((Bitmap)eventArgs.Frame.Clone());
                #pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                //  Это выкинуть в отдельный поток!
                //  И отдать делегат? Или просто проверять значение переменной?
                //  Тут хрень какая-то

                //currentState = Stage.Thinking;
                //sage.solveState(processor.currentDeskState, 16, 7);
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (videoSource == null)
            {
                var vcd = new VideoCaptureDevice(videoDevicesList[cmbVideoSource.SelectedIndex].MonikerString);
                vcd.VideoResolution = vcd.VideoCapabilities[resolutionsBox.SelectedIndex];
                Debug.WriteLine(vcd.VideoCapabilities[1].FrameSize.ToString());
                Debug.WriteLine(resolutionsBox.SelectedIndex);
                videoSource = vcd;
                videoSource.NewFrame += new NewFrameEventHandler(video_NewFrame);
                videoSource.Start();
                StartButton.Text = "Стоп";
                controlPanel.Enabled = true;
                cmbVideoSource.Enabled = false;
            }
            else
            {
                videoSource.SignalToStop();
                if (videoSource != null && videoSource.IsRunning && originalImageBox.Image != null)
                {
                    originalImageBox.Image.Dispose();
                }
                videoSource = null;
                StartButton.Text = "Старт";
                controlPanel.Enabled = false;
                cmbVideoSource.Enabled = true;
            }
        }

        private void tresholdTrackBar_ValueChanged(object sender, EventArgs e)
        {
            controller.settings.threshold = (byte)tresholdTrackBar.Value;
            controller.settings.differenceLim = (float)tresholdTrackBar.Value/tresholdTrackBar.Maximum;
        }

        private void borderTrackBar_ValueChanged(object sender, EventArgs e)
        {
            controller.settings.border = borderTrackBar.Value;
        }

        private void marginTrackBar_ValueChanged(object sender, EventArgs e)
        {
            controller.settings.margin = marginTrackBar.Value;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (updateTmr != null)
                updateTmr.Dispose();

            //  Как-то надо ещё робота подождать, если он работает

            if (videoSource != null && videoSource.IsRunning)
            {
                videoSource.SignalToStop();
            }
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            switch(e.KeyCode)
            {
                case Keys.W: controller.settings.decTop(); Debug.WriteLine("Up!"); break;
                case Keys.S: controller.settings.incTop(); Debug.WriteLine("Down!"); break;
                case Keys.A: controller.settings.decLeft(); Debug.WriteLine("Left!"); break;
                case Keys.D: controller.settings.incLeft(); Debug.WriteLine("Right!"); break;
                case Keys.Q: controller.settings.border++; Debug.WriteLine("Plus!"); break;
                case Keys.E: controller.settings.border--; Debug.WriteLine("Minus!"); break;
            }
        }

        private void cmbVideoSource_SelectionChangeCommitted(object sender, EventArgs e)
        {
            var vcd = new VideoCaptureDevice(videoDevicesList[cmbVideoSource.SelectedIndex].MonikerString);
            resolutionsBox.Items.Clear();
            for (int i = 0; i < vcd.VideoCapabilities.Length; i++)
                resolutionsBox.Items.Add(vcd.VideoCapabilities[i].FrameSize.ToString());
            resolutionsBox.SelectedIndex = 0;
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            controller.settings.processImg = checkBox1.Checked;
        }
        // ===========================================================================================


        /// <summary>
        /// Генератор изображений (образов)
        /// </summary>
        //GenerateImage generator = new GenerateImage();

        /// <summary>
        /// Текущая выбранная через селектор нейросеть
        /// </summary>
        public BaseNetwork Net
        {
            get
            {
                var selectedItem = (string)netTypeBox.SelectedItem;
                if (!networksCache.ContainsKey(selectedItem))
                    networksCache.Add(selectedItem, CreateNetwork(selectedItem));

                return networksCache[selectedItem];
            }
        }

        private Dictionary<string, Func<int[], BaseNetwork>> networksFabric;
        private Dictionary<string, BaseNetwork> networksCache = new Dictionary<string, BaseNetwork>();

        /// <summary>
        /// Конструктор формы стенда для работы с сетями
        /// </summary>
        /// <param name="networksFabric">Словарь функций, создающих сети с заданной структурой</param>
        public void NeuralNetworksStand(Dictionary<string, Func<int[], BaseNetwork>> networksFabric)
        {
            InitializeComponent();
            this.networksFabric = networksFabric;
            netTypeBox.Items.AddRange(this.networksFabric.Keys.Select(s => (object)s).ToArray());
            netTypeBox.SelectedIndex = 0;
            button3_Click(this, null);
        }

        public void UpdateLearningInfo(double progress, double error, TimeSpan elapsedTime)
        {
            if (progressBar1.InvokeRequired)
            {
                progressBar1.Invoke(new TrainProgressHandler(UpdateLearningInfo), progress, error, elapsedTime);
                return;
            }

            StatusLabel.Text = "Ошибка: " + error;
            int progressPercent = (int)System.Math.Round(progress * 100);
            progressPercent = System.Math.Min(100, System.Math.Max(0, progressPercent));
            elapsedTimeLabel.Text = "Затраченное время : " + elapsedTime.Duration().ToString(@"hh\:mm\:ss\:ff");
            progressBar1.Value = progressPercent;
        }


        private void set_result(Sample figure)
        {
            label1.ForeColor = figure.Correct() ? Color.Green : Color.Red;

            label1.Text = "Распознано : " + figure.recognizedClass;

            label9.Text = string.Join("\n", figure.Output.Select(d => d.ToString()));
           
        }


        private async Task<double> train_networkAsync(int training_size, int epoches, double acceptable_error,
            bool parallel = true)
        {
            //  Выключаем всё ненужное
            label1.Text = "Выполняется обучение...";
            label1.ForeColor = Color.Red;
            groupBox1.Enabled = false;

            //  Создаём новую обучающую выборку
            SamplesSet samples = CreateSamplesSet();
            try
            {
                //  Обучение запускаем асинхронно, чтобы не блокировать форму
                var curNet = Net;
                double f = await Task.Run(() => curNet.TrainOnDataSet(samples, epoches, acceptable_error, parallel));
                StreamWriter nnFile = File.CreateText(@"..\..\NN.txt");
                var s = Newtonsoft.Json.JsonConvert.SerializeObject(Net);
                nnFile.WriteLine(s);
                nnFile.Flush();
                nnFile.Close();
                label1.Text = "Покажите картинку";
                label1.ForeColor = Color.Green;
                groupBox1.Enabled = true;
               // pictureBox1.Enabled = true;
               // trainOneButton.Enabled = true;
                StatusLabel.Text = "Ошибка: " + f;
                //StatusLabel.ForeColor = Color.Green;
                return f;
            }
            catch (Exception e)
            {
                throw e;
                label1.Text = $"Исключение: {e.Message}";
            }

            return 0;
        }


        private void button2_Click(object sender, EventArgs e)
        {
            Enabled = false;
            //  Тут просто тестирование новой выборки
            //  Создаём новую обучающую выборку
            SamplesSet samples = CreateSamplesSet();

            double accuracy = samples.TestNeuralNetwork(Net);

            StatusLabel.Text = $"Точность на тестовой выборке : {accuracy * 100,5:F2}%";
            StatusLabel.ForeColor = accuracy * 100 >= AccuracyCounter.Value ? Color.Green : Color.Red;

            Enabled = true;
        }

        private void button3_Click(object sender, EventArgs e)
        {
           
        }

        private int[] CurrentNetworkStructure()
        {
            return netStructureBox.Text.Split(';').Select(int.Parse).ToArray();
        }

        private void classCounter_ValueChanged(object sender, EventArgs e)
        {
            //generator.FigureCount = (int)classCounter.Value;
            var vals = netStructureBox.Text.Split(';');
            if (!int.TryParse(vals.Last(), out _)) return;
            vals[vals.Length - 1] = classCounter.Value.ToString();
            netStructureBox.Text = vals.Aggregate((partialPhrase, word) => $"{partialPhrase};{word}");
        }

        private BaseNetwork CreateNetwork(string networkName)
        {
            var network = networksFabric[networkName](CurrentNetworkStructure());
            network.TrainProgress += UpdateLearningInfo;
            return network;
        }
        
        private void netTrainButton_Click(object sender, EventArgs e)
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            train_networkAsync((int)TrainingSizeCounter.Value, (int)EpochesCounter.Value,
                (100 - AccuracyCounter.Value) / 100.0, parallelCheckBox.Checked);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        private void recreateNetButton_Click(object sender, EventArgs e)
        {
            //  Проверяем корректность задания структуры сети
            int[] structure = CurrentNetworkStructure();
            if (structure.Length < 2  ||
                structure[structure.Length - 1] != 5 || structure[0] != 400)
            {
                MessageBox.Show(
                    $"В сети должно быть более двух слоёв, первый слой должен содержать 400 нейронов, последний - 5",
                    "Ошибка", MessageBoxButtons.OK);
                return;
            }

            // Чистим старые подписки сетей
            foreach (var network in networksCache.Values)
                network.TrainProgress -= UpdateLearningInfo;
            // Пересоздаём все сети с новой структурой
            networksCache = networksCache.ToDictionary(oldNet => oldNet.Key, oldNet => CreateNetwork(oldNet.Key));
        }

        private void testNetButton_Click(object sender, EventArgs e)
        {
            Enabled = false;
            //  Тут просто тестирование новой выборки
            //  Создаём новую обучающую выборку
            SamplesSet samples = CreateSamplesSet();
            double accuracy = samples.TestNeuralNetwork(Net);

            StatusLabel.Text = $"Точность на тестовой выборке : {accuracy * 100,5:F2}%";
            StatusLabel.ForeColor = accuracy * 100 >= AccuracyCounter.Value ? Color.Green : Color.Red;

            Enabled = true;
        }

        private void ProcessButton_Click(object sender, EventArgs e)
        {
            var sample = CreateSample();
            Net.Predict(sample);
            set_result(sample);
        }

        public SamplesSet CreateSamplesSet()
        {
            SamplesSet result = new SamplesSet();
            /* try
             {
                 var s = System.IO.File.ReadAllText(@"../../samples.txt");
                 result = JsonConvert.DeserializeObject<SamplesSet>(s);
             }
             catch (Exception ex)
             {
                 throw ex;
             }*/
            //result = FileHelper.ReadFromBinaryFile<SamplesSet>(@"..\..\classes.data");
            ProcessClassSamples(result, Directory.GetFiles(@"..\..\Images\Play"), FigureType.Play);
            ProcessClassSamples(result, Directory.GetFiles(@"..\..\Images\Stop"), FigureType.Stop);
            ProcessClassSamples(result, Directory.GetFiles(@"..\..\Images\Pause"), FigureType.Pause);
            ProcessClassSamples(result, Directory.GetFiles(@"..\..\Images\Forward"), FigureType.Forward);
            ProcessClassSamples(result, Directory.GetFiles(@"..\..\Images\Backward"), FigureType.Backward);
            FileHelper.WriteToBinaryFile(@"..\..\classes.data", result);
            /*StreamWriter nnFile = File.CreateText(@"..\..\samples.txt");
            var s = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            nnFile.WriteLine(s);
            nnFile.Flush();
            nnFile.Close();*/
            return result;
        }

        private void ProcessClassSamples(SamplesSet samplesSet, string[] fileNames, FigureType type)
        {
            try
            {
 
                foreach (var fileName in fileNames)
                {
                    var image = new Bitmap(fileName);
                    var inputs = new double[400];
                    for (int i = 0; i < 200; i++)
                    {
                        inputs[i] = CountBlackPixels(GetBitmapColumn(image, i));
                        inputs[i + 200] = CountBlackPixels(GetBitmapRow(image, i));
                        
                    }
                    samplesSet.AddSample(new Sample(inputs, 5, type));
                   
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public Sample CreateSample(FigureType actualType = FigureType.Undef)
        {
            var inputs = new double[400];
            var img = new Bitmap(processedImgBox.Image);
            for (int i = 0; i < 200; i++)
            {
                inputs[i] = CountBlackPixels(GetBitmapColumn(img, i));
                inputs[i + 200] = CountBlackPixels(GetBitmapRow(img, i));
            }

            return new Sample(inputs, 5, actualType);
        }

        public int CountBlackPixels(Color[] pixels) =>
            pixels.Count(p => p.R < 0.1 && p.G < 0.1 && p.B < 0.1);

        public Color[] GetBitmapColumn(Bitmap picture, int ind)
        {
            var result = new Color[picture.Height];
            for (int i = 0; i < picture.Height; i++)
                result[i] = picture.GetPixel(ind, i);
            return result;
        }

        public Color[] GetBitmapRow(Bitmap picture, int ind)
        {
            var result = new Color[picture.Width];
            for (int i = 0; i < picture.Width; i++)
                result[i] = picture.GetPixel(i, ind);
            return result;
        }

        //TODO: обрезать
        private void button1_Click(object sender, EventArgs e)
        {
            if (!Directory.Exists("..\\..\\Images"))
                Directory.CreateDirectory("..\\..\\Images");
            FigureType f = 0;
            for (int i = 0; i < 5; i++)
            {
                if (!Directory.Exists("..\\..\\Images\\" + f.ToString()))
                    Directory.CreateDirectory("..\\..\\Images\\" + f.ToString());
                f++;
            }
            FigureType current_letter = 0;
            switch (comboBox1.Text)
            {
                case "Play": current_letter = 0; break;
                case "Stop": current_letter = (FigureType)1; break;
                case "Pause": current_letter = (FigureType)2; break;
                case "Backward": current_letter = (FigureType)3; break;
                case "Forward": current_letter = (FigureType)4; break;
                case "Undef": current_letter = (FigureType)5; break;
            }
            var lst = Directory.GetFiles("..\\..\\Images\\" + current_letter.ToString());
            processedImgBox.Image.Save("..\\..\\Images\\" + current_letter.ToString() + "\\processed_"+ current_letter.ToString().ToLower() + lst.Length + ".png", System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    }

