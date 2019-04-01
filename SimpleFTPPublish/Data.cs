using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml.Serialization;


namespace Bizcacha.App {
    [Serializable]
    public class Data {
        public List<Archivo> Archivos { get; set; }
        public List<String> Carpetas { get; set; }

        public virtual bool Save() {
            try {
                StreamWriter write = new StreamWriter(AppFolderAndFile());
                XmlSerializer xml = new XmlSerializer(GetType());
                xml.Serialize(write, this);
                write.Close();
            }
            catch {
                return false;
            }
            return true;
        }

        // Load settings from file.
        public static Data Load() {
            Data retval;
            try {
                StreamReader reader = new StreamReader(AppFolderAndFile());
                XmlSerializer xml = new XmlSerializer(typeof(Data));
                retval = (Data)xml.Deserialize(reader);
                reader.Close();
            }
            catch (Exception ex) {
                retval = new Data();
            }

            if (retval.Archivos == null) {
                retval.Archivos = new List<Archivo>();
            }
            if (retval.Carpetas == null) {
                retval.Carpetas = new List<String>();
            }
            return retval;
        }

        public static string AppFolder() {
            return System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        static string AppFolderAndFile() {
            return AppFolder() + "\\SimpleFTPPublish.XML"; 
        }

    }

    [Serializable]
    public class Archivo {
        public string Nombre { get; set; }
        public DateTime Fecha { get; set; }
        public long Tamanio { get; set; }
    }

}
