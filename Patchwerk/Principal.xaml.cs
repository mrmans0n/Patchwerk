using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.IO;
using System.Threading;
using System.Diagnostics;
using ICSharpCode.SharpZipLib.Zip;
using System.Collections;

namespace Patchwerk
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class Window1 : Window
    {
        private delegate void ActualizarEstado(string args);
        private delegate void BotonActivado(bool args);
        private delegate void IniciarEjecucion(string args);
        private delegate void GenerarBatch();
        private delegate void ComprimirParche(string xdelta, string parche, string batch, bool linux);
        private delegate void BorrarIntermedios();

        private readonly string _xdelta = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xdelta3.0u.x86-32.exe");

        private Object _lock1 = new Object();
        private char[] _invalidos = System.IO.Path.GetInvalidPathChars();

        public Window1()
        {
            InitializeComponent();
            Assembly ass  = Assembly.GetExecutingAssembly();
            FileVersionInfo vinfo = FileVersionInfo.GetVersionInfo(ass.Location);
            this.Title = string.Format("Patchwerk {0}.{1}.{2}.{3}", vinfo.FileMajorPart, vinfo.FileMinorPart, vinfo.FileBuildPart, vinfo.FilePrivatePart);
            this.checkBox2.Unchecked += new System.Windows.RoutedEventHandler(this.checkBox2_Checked);
            this.Closing += new System.ComponentModel.CancelEventHandler(Window1_Closing);

            this.checkBox1.IsChecked = Properties.Settings.Default.DeleteIntermediate;
            this.checkBox2.IsChecked = Properties.Settings.Default.Compress;
            this.checkBox3.IsChecked = Properties.Settings.Default.UnixShellScript;
        }

        void Window1_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Properties.Settings.Default.DeleteIntermediate = this.checkBox1.IsChecked.Value;
            Properties.Settings.Default.Compress = this.checkBox2.IsChecked.Value;
            Properties.Settings.Default.UnixShellScript = this.checkBox3.IsChecked.Value;
            Properties.Settings.Default.Save();
        }

        private void ErrorMsg(string errName)
        {
            MessageBox.Show(errName, this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private bool HasErrors()
        {
            // comprobaciones

            if (!File.Exists(_xdelta))
            {
                ErrorMsg("No se ha encontrado 'xdelta3.0u.x86-32.exe'. No se puede continuar.");
                return true;
            }

            if (ArchivoOrigen.Text == "")
            {
                ErrorMsg("No se ha introducido un archivo de origen.");
                return true;
            }

            if (ArchivoDestino.Text == "")
            {
                ErrorMsg("No se ha introducido un archivo de destino.");
                return true;
            }

            if (NombreParche.Text == "")
            {
                ErrorMsg("No se ha introducido un nombre para el parche.");
                return true;
            }

            if (BatchDestino.Text == "")
            {
                ErrorMsg("No se ha introducido un archivo .BAT de destino.");
                return true;
            }

            if (!BatchDestino.Text.EndsWith(".bat", StringComparison.InvariantCultureIgnoreCase))
            {
                ErrorMsg("No se ha guardado el .bat con extensión adecuada.");
                return true;
            }

            if (!File.Exists(ArchivoOrigen.Text))
            {
                ErrorMsg("No existe el archivo de origen.");
                return true;
            }

            if (!File.Exists(ArchivoDestino.Text))
            {
                ErrorMsg("No existe el archivo de destino.");
                return true;
            }

            return false;
        }

        private void ChangeStatus(string newStatus)
        {
            Estado.Content = newStatus;
        }

        private void ChangeButtonEnabled(bool value)
        {
            button3.IsEnabled = value;
        }

        private void DeleteTemporalFiles()
        {
            if (checkBox1.IsChecked != null) if (!checkBox1.IsChecked.Value) return;
            string patch = NombreParche.Text+".xdelta";
            string batch = BatchDestino.Text;

            if (File.Exists(patch)) File.Delete(patch);
            if (File.Exists(batch)) File.Delete(batch);
            if (File.Exists(batch.Replace(".bat", ".sh"))) File.Delete(batch.Replace(".bat", ".sh"));
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.CheckFileExists = true;
            if (ofd.ShowDialog().HasValue)
            {
                ArchivoOrigen.Text = ofd.FileName;
            }

        }

        private void button2_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.CheckFileExists = true;
            if (ofd.ShowDialog().HasValue)
            {
                ArchivoDestino.Text = ofd.FileName;
            }
        }

        private void button4_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog ofd = new SaveFileDialog();
            ofd.Filter = "Archivos de procesos por lotes (*.bat)|*.bat";
            ofd.AddExtension = true;
            ofd.ValidateNames = true;
            if (ofd.ShowDialog().HasValue)
            {
                BatchDestino.Text = ofd.FileName;
            }
        }



        private void button3_Click(object sender, RoutedEventArgs e)
        {
            if (HasErrors()) return;

            BatchDestino.Text = FixNonValidCharacters(BatchDestino.Text);

            ChangeButtonEnabled(false);
            
            FileInfo fi = new FileInfo(BatchDestino.Text);
            Directory.SetCurrentDirectory(fi.DirectoryName);

            ChangeStatus("Creando el parche...");

            IniciarEjecucion ex = new IniciarEjecucion(EjecutarXdelta);
            string argumentos = "-e -0 -B 536870912 -s \"" + ArchivoOrigen.Text + "\" \"" + ArchivoDestino.Text + "\" \"" + NombreParche.Text + ".xdelta\"";
            ex.BeginInvoke(argumentos,null, null);

        }

        private void EjecutarXdelta(string args)
        {
            Process proc = new Process();
            ProcessStartInfo procsinfo = new ProcessStartInfo(_xdelta);
            procsinfo.Arguments = args;
            proc.StartInfo = procsinfo;
            procsinfo.CreateNoWindow = true;
            procsinfo.UseShellExecute = false;
            proc.EnableRaisingEvents = true;
            //proc.SynchronizingObject = this;

            proc.Start();

            proc.Exited += new EventHandler(proc_Exited);
        }

        private void CreateBatch()
        {
            FileInfo fi = new FileInfo(ArchivoOrigen.Text);
            string origen = fi.Name;
            fi = new FileInfo(ArchivoDestino.Text);
            string destino = fi.Name;
            
            fi = new FileInfo(_xdelta);
            string xdeltaLocal = fi.Name;

            fi = new FileInfo(BatchDestino.Text);
            string shBatch = fi.Name.Replace(".bat", ".sh");

            fi = new FileInfo(NombreParche.Text);
            string parche = fi.Name+".xdelta";



            TextWriter str = File.CreateText(BatchDestino.Text);
            str.WriteLine("@echo off");
            str.WriteLine("echo Archivo generado con Patchwerk (c) mrm/AnimeUnderground");
            str.WriteLine("echo.");
            str.WriteLine("if not exist \"" + origen + "\" goto nofile");
            str.WriteLine("echo Archivo de origen: "+origen);
            str.WriteLine("echo Archivo de destino: "+destino);
            str.WriteLine("echo Aplicando parche...");
            str.WriteLine(xdeltaLocal + " -f -d -B 268435456 -s  \"" + origen + "\" \"" + parche + "\" \"" + destino + "\"");
            str.WriteLine("echo Parche aplicado.");
            str.WriteLine("pause");
            str.WriteLine("exit");
            str.WriteLine(":nofile");
            str.WriteLine("echo No se ha encontrado el archivo de origen en el directorio.");
            str.WriteLine("pause");
            str.WriteLine("exit");
            str.Close();

            if (checkBox3.IsChecked != null)
                if (checkBox3.IsChecked.Value)
                {
                    str = File.CreateText(shBatch);
                    str.Write("#!/usr/bin/env bash\n\n");
                    //str.Write();
                    str.Write("echo \"Archivo generado con Patchwerk (c) mrm/AnimeUnderground\"\n");
                    str.Write("echo\n");
                    str.Write("if [[ ! $(which xdelta3) ]]; then\n");
                    str.Write("	echo \"Es necesario tener instalado xdelta3. Saliendo.\"\n");
                    str.Write("	exit -1\n");
                    str.Write("else\n");
                    str.Write("if [[ ! -f \"" + origen + "\" ]]; then\n");
                    str.Write("		echo \"No se encuentra el fichero de origen: " + origen + "\"\n");
                    str.Write("		exit -1\n");
                    str.Write("	fi\n\n"); 
                    //str.Write();
                    str.Write("if [[ ! -f \"" + parche + "\" ]]; then\n");
                    str.Write("		echo \"No se encuentra el parche: " + parche + "\"\n");
                    str.Write("		exit -1\n");
                    str.Write("	fi\n\n");
                    //str.Write();
                    str.Write("	echo \"Archivo de origen: " + origen + "\"\n");
                    str.Write("	echo \"Archivo de destino: " + destino + "\"\n");
                    str.Write("	echo \"Aplicando parche...\"\n");
                    str.Write("	xdelta3 -f -d -B 268435456 -s \"" + origen + "\" \"" + parche + "\" \"" + destino + "\"\n");
                    str.Write("	echo \"Parche aplicado.\"\n");
                    str.Write("fi\n\n");
                    //str.Write();
                    str.Write("exit 0\n");
                    str.Close();

                }

            ChangeStatus("Comprimiendo los archivos necesarios...");

            if (checkBox2.IsChecked != null)
                if (checkBox2.IsChecked.Value)
                {
                    ComprimirParche cpatch = new ComprimirParche(CompressFiles);
                    if (checkBox3.IsChecked != null)
                        cpatch.BeginInvoke(_xdelta, NombreParche.Text + ".xdelta", BatchDestino.Text, checkBox3.IsChecked.Value, null, null);
                }
                else
                {
                    File.Copy(_xdelta, System.IO.Path.Combine(fi.DirectoryName, xdeltaLocal));
                    ChangeStatus("Parche realizado satisfactoriamente.");
                    ChangeButtonEnabled(true);
                }
        }

        private void CompressFiles(string xdelta, string patch, string batch, bool linux)
        {

            FileInfo crap = new FileInfo(batch);
            string zipName = crap.Name.Substring(0, Math.Max(crap.Name.Length - crap.Extension.Length, 0));
            if (zipName == "") zipName = "Zip del parche.zip";
            else zipName += ".zip";

            ArrayList compArchivos = new ArrayList();
            compArchivos.Add(xdelta);
            compArchivos.Add(patch);
            compArchivos.Add(batch);
            if (linux)
                compArchivos.Add(batch.Replace(".bat", ".sh"));

            FileStream zipFile = File.Create(zipName);
            using (ZipOutputStream output = new ZipOutputStream(zipFile))
            {
                foreach (string fil in compArchivos)
                {
                    FileInfo fi = new FileInfo(fil);
                    ZipEntry ze = new ZipEntry(fi.Name);
                    //ze.CompressionMethod = (fil.Equals(xdelta))? CompressionMethod.Stored : CompressionMethod.BZip2;
                    FileStream comp = File.OpenRead(fil);
                    byte[] buffer = new byte[Convert.ToInt32(comp.Length)];
                    comp.Read(buffer, 0, (int)comp.Length);
                    ze.DateTime = fi.LastWriteTime;
                    ze.Size = fi.Length;
                    comp.Close();
                    output.PutNextEntry(ze);
                    output.Write(buffer, 0, buffer.Length);
                }
                output.Finish();
                output.Close();
            }

            // borrar archivos intermedios
            //File.Delete(patch);
            //File.Delete(batch);
            this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new BorrarIntermedios(DeleteTemporalFiles));
            this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new ActualizarEstado(ChangeStatus), "Parche realizado satisfactoriamente.");
            this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new BotonActivado(ChangeButtonEnabled), true);
        }

        void proc_Exited(object sender, EventArgs e)
        {
            this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new ActualizarEstado(ChangeStatus), "Creando BAT...");
            this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new GenerarBatch(CreateBatch));            
        }

        private void checkBox2_Checked(object sender, RoutedEventArgs e)
        {
            if (checkBox2.IsChecked != null) checkBox1.IsEnabled = checkBox2.IsChecked.Value;
        }

        private void ArchivoOrigenDestinoCambiado(object sender, TextChangedEventArgs e)
        {
            if (ArchivoDestino.Text!="" && ArchivoOrigen.Text!="")
            {
                // origen y destino iguales
            }
        }

        private void PatchNameChanged(object sender, TextChangedEventArgs e)
        {
            lock (_lock1)
            {
                int oldparchepos = NombreParche.SelectionStart;
                int olddestinopos = ArchivoDestino.SelectionStart;

                string parche = FixNonValidCharacters(NombreParche.Text);
                string destino  = FixNonValidCharacters(ArchivoDestino.Text);
                
                NombreParche.Text = parche;
                ArchivoDestino.Text = destino;

                NombreParche.SelectionStart = oldparchepos;
                ArchivoDestino.SelectionStart = olddestinopos;

                if (destino != "" && parche != "")
                {
                    FileInfo finfo = new FileInfo(destino);
                    BatchDestino.Text = System.IO.Path.Combine(finfo.DirectoryName, parche + ".bat");
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            automaticUpdater1.ForceCheckForUpdate();
        }

        private string FixNonValidCharacters(string oldstring)
        {
            string res = oldstring;            
            foreach (char c in _invalidos)
                res = res.Replace(c, '_');
            return res;
        }


        private void ArchivoDestino_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop,true))
            {
                e.Effects = DragDropEffects.All;
                e.Handled = true;
            }
        }


        private void ArchivoDestino_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop,true))
            {
                string[] fileNames = e.Data.GetData(DataFormats.FileDrop, true) as string[];
                if (fileNames != null) ArchivoDestino.Text = fileNames[0];
            }
        }



        private void ArchivoOrigen_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, true))
            {
                e.Effects = DragDropEffects.All;
                e.Handled = true;
            }
        }

        private void ArchivoOrigen_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, true))
            {
                string[] fileNames = e.Data.GetData(DataFormats.FileDrop, true) as string[];
                if (fileNames != null) ArchivoOrigen.Text = fileNames[0];
            }
        }

    }
}
