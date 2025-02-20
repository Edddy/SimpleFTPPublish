using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Collections;
using System.Net;
using System.Diagnostics;


// pendiente de implementar la borrada en el servidor de archivos 
// que dejaron de existir

namespace Bizcacha.App {
    static class Program {

        static Data MyData;
        static string TargetURL;
        static string Usuario;
        static string Clave;
        static int Procesados = 0;
        static int Subidos = 0;
        static int SinModificacion = 0;
        static int ConErrorAlSubir = 0;
        static bool Pasivo = true;
        static bool DontUploadOnlyCreateLocalData = false;

        static void Main(string[] args) {

            ResetLog();

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            Log("---------- App starting ----------");
            // read the main configuration
            TargetURL = Properties.Settings.Default.TargetURL;
            Usuario = Properties.Settings.Default.Usuario;
            Clave = Properties.Settings.Default.Clave;
            Pasivo = Properties.Settings.Default.Pasivo;
            for (int i = 0; i < args.Length; i++) {
                if (args[i].ToLower() == "-dontupload") DontUploadOnlyCreateLocalData = true;
                if (args[i].ToString().Contains("?")) { 
                    Log("-dontupload: Graba el log pero no sube nada");
                    return;
                };
            }
            
            Log("Uploading to " + TargetURL);
            
            // levanto los datos
            Log("Loading cache...");
            MyData = Data.Load();

            // upload appOffline
            //Subir("app_offline-template.htm", "app_offline.htm");
            
            // empiezo a procesar todos las carpetas
            Log("Processing...");
            WalkDirectoryTree(Data.AppFolder(), false);

            // Delete appOffline
            //DeleteOnTarget("app_offline.htm");

            stopwatch.Stop();

            if (ConErrorAlSubir >0 ) {
                Log(String.Format("=== {0} Files with upload errors ===", ConErrorAlSubir));
            }

            Log(String.Format("Files processed: {0}. Uploaded: {1}. Unmodified: {2}. With upload errors: {3}. Time Elapsed: {4}", Procesados, Subidos, SinModificacion, ConErrorAlSubir, stopwatch.Elapsed));

            Log("Saving cache...");
            MyData.Save();

            //chau
            Log("---------- The End ----------");
        }


        public static IEnumerable<string> GetFileList(string fileSearchPattern, string rootFolderPath) {
            Queue<string> pending = new Queue<string>();
            pending.Enqueue(rootFolderPath);
            string[] tmp;
            while (pending.Count > 0) {
                rootFolderPath = pending.Dequeue();
                tmp = Directory.GetFiles(rootFolderPath, fileSearchPattern);
                for (int i = 0; i < tmp.Length; i++) {
                    yield return tmp[i];
                }
                tmp = Directory.GetDirectories(rootFolderPath);
                for (int i = 0; i < tmp.Length; i++) {
                    pending.Enqueue(tmp[i]);
                }
            }
        }


        static void WalkDirectoryTree(string currentDir, bool skipFiles ) {

            if (FolderIsInFilter(currentDir.Substring(Data.AppFolder().Length))) return;

            Console.WriteLine(currentDir);

            string[] files = null;
            string[] subDirs = null;

            // First, process all the files directly under this folder
            try {
                files = System.IO.Directory.GetFiles(currentDir);
                subDirs = System.IO.Directory.GetDirectories(currentDir);
            }
            // This is thrown if even one of the files requires permissions greater
            // than the application provides.
            catch (UnauthorizedAccessException e) {
                // This code just writes out the message and continues to recurse.
                // You may decide to do something different here. For example, you
                // can try to elevate your privileges and access the file again.
                // log.Add(e.Message);
            }

            catch (System.IO.DirectoryNotFoundException e) {
                Console.WriteLine(e.Message);
            }

            // chequeo si existe
            CheckFolder(currentDir);

            if (files != null && !skipFiles) {
                foreach (string file in files) {
                    ProcessFile(file);
                }
            }

            if (subDirs != null) {
                foreach (string dir in subDirs) {
                    // Resursive call for each subdirectory.
                    WalkDirectoryTree(dir, false);
                }
            }

        }
        static void ProcessFile(string file) {
            System.IO.FileInfo fi = new System.IO.FileInfo(file);

            // check filter
            if (FileIsInFilter(fi)) return;

            Procesados++;
            string RelativeName = fi.FullName.Substring(Data.AppFolder().Length);
            Archivo ArchivoEnData = (from rec in MyData.Archivos where rec.Nombre == RelativeName select rec).FirstOrDefault();
            if (ArchivoEnData == null) {
                if (Subir(file)) {
                    MyData.Archivos.Add(new Archivo { Nombre = RelativeName, Fecha = fi.LastWriteTime, Tamanio = fi.Length });
                    Subidos++;
                }
            }
            else if (ArchivoEnData.Tamanio != fi.Length || ArchivoEnData.Fecha != fi.LastWriteTime) {
                if (Subir(file)) {
                    ArchivoEnData.Tamanio = fi.Length;
                    ArchivoEnData.Fecha = fi.LastWriteTime;
                    Subidos++;
                }
            }
            else {
                SinModificacion++;
            }

        }
        static bool Subir(string file, string target = "") {
            if (DontUploadOnlyCreateLocalData) return true;

            try {

                // creo el target sacando el root folder y poniento el target URL
                if (target == "") {
                    target = TargetURL + file.Replace("\\", "/").Substring(Data.AppFolder().Length);
                }

                // empiezo la subida
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(target);
                request.Method = WebRequestMethods.Ftp.UploadFile;
                request.Credentials = new NetworkCredential(Usuario, Clave);
                request.UsePassive = Pasivo;

                // Copy the contents of the file to the request stream.
                byte[] fileContents = System.IO.File.ReadAllBytes(file); 
                request.ContentLength = fileContents.Length;

                Stream requestStream = request.GetRequestStream();
                requestStream.Write(fileContents, 0, fileContents.Length);
                requestStream.Close();

                FtpWebResponse response = (FtpWebResponse)request.GetResponse();

                //Log(response.StatusDescription.ToString());

                return true;

            }
            catch (Exception ex) {
                Log("Error uploading file " + file + ". Target: " + target + ". Error: " + ex.Message);
                ConErrorAlSubir++;
                return false;
            }

        }

        static void CheckFolder(string directoryPath) {
            string relative = directoryPath.Substring(Data.AppFolder().Length);
            if (!string.IsNullOrEmpty(relative)) {
                if ((from rec in MyData.Carpetas where rec == relative select rec).SingleOrDefault() == null) {
                    CheckRemoteFolder(directoryPath);
                    MyData.Carpetas.Add(relative);
                }
            }
        }

        static void CheckRemoteFolder(string directoryPath) {
            // creo el target sacando el root folder y poniento el target URL
            string target = TargetURL + directoryPath.Replace("\\", "/").Substring(Data.AppFolder().Length) + "/";

            try {
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(target);
                request.Credentials = new NetworkCredential(Usuario, Clave);
                request.Method = WebRequestMethods.Ftp.MakeDirectory;
                request.UsePassive = Pasivo;

                FtpWebResponse response = (FtpWebResponse)request.GetResponse();
            }
            catch (WebException ex) {
                // seguramente ya existia, no hago nada
            }

        }

        static bool FolderIsInFilter(string folder) {
            var foldername = folder.ToLower();
            return (foldername == "\\obj" 
				|| foldername == "\\content\\subidos" 
				|| foldername == "\\.git" 
				|| foldername == "\\.vs" 
				|| foldername == "\\properties" 
				|| foldername == "\\resources");
        }

        static bool FileIsInFilter(System.IO.FileInfo fi) {
            var name = fi.Name.ToLower();
            var ext = fi.Extension.ToLower();
            return (name == "web.config" 
				|| name == "log.txt" 
				|| name == "config.json" 
				|| name == "thumbs.db" 
				|| name == "simpleftppublish.exe.config" 
				|| name == "simpleftppublish.xml" 
				|| ext == ".cs" 
				|| ext == ".vb" || ext == ".resx" || ext == ".csproj" || ext == ".user");
        }

        public static void Log(string msg) {
            System.IO.StreamWriter sw = System.IO.File.AppendText(Data.AppFolder() + "\\Log.txt");
            try {
                string logLine = System.String.Format(
                    "{0:G}: {1}.", System.DateTime.Now, msg);
                sw.WriteLine(logLine);
                Console.WriteLine(logLine);
            }
            finally {
                sw.Close();
            }
        }

        public static void ResetLog() {
            try {
                System.IO.File.Delete(Data.AppFolder() + "\\Log.txt");
            }
            catch {
                // no hago nada
            }
                
        }

    }
}
