// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.
//
// Import the Mozilla's trusted root certificates
// from https://github.com/mono/mono/blob/master/mcs/tools/security/mozroots.cs
//

using ICSharpCode.SharpZipLib.Zip;
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using UnityEngine;


namespace Fun
{
    public class MozRoots
    {
        public static bool DownloadMozRoots ()
        {
            string tempPath = FunapiUtils.GetLocalDataPath + "/" + Path.GetRandomFileName();
            try
            {
                // Try to download the Mozilla's root certificates file.
                WebClient webClient = new WebClient();
                webClient.DownloadFile(kMozillaCertificatesUrl, tempPath);
            }
            catch (WebException we)
            {
                FunDebug.LogError("Download certificates failed. {0}", we.Message);
                File.Delete(tempPath);
                return false;
            }

            try
            {
                using (FileStream readStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] buffer = new byte[readStream.Length];
                    readStream.Read(buffer, 0, buffer.Length);
                    readStream.Close();
                    File.Delete(tempPath);

                    string targetPath = FunapiUtils.GetAssetsPath + kDownloadCertificatesPath;

                    // If there's certificates file, delete the file.
                    if (File.Exists(targetPath))
                        File.Delete(targetPath);

                    using (FileStream writeStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Write))
                    {
                        using (ZipOutputStream zipOutputStream = new ZipOutputStream(writeStream))
                        {
                            ZipEntry zipEntry = new ZipEntry("MozRoots");
                            zipEntry.Size = buffer.Length;
                            zipEntry.DateTime = DateTime.Now;

                            zipOutputStream.PutNextEntry(zipEntry);
                            zipOutputStream.Write(buffer, 0, buffer.Length);
                            zipOutputStream.Close();
                        }
                        writeStream.Close();
                    }
                }
            }
            catch (Exception e)
            {
                FunDebug.LogError("The creation of the zip file of certificates failed. {0}", e.Message);
                return false;
            }

            return true;
        }

        public static void LoadRootCertificates ()
        {
            if (trustedCerificates != null)
                return;

            try
            {
                TextAsset zippedMozRootsRawData = Resources.Load<TextAsset>(kResourceCertificatesPath);
                if (zippedMozRootsRawData == null)
                    throw new System.Exception("Certificates file does not exist!");
                if (zippedMozRootsRawData.bytes == null || zippedMozRootsRawData.bytes.Length <= 0)
                    throw new System.Exception("The certificates file is corrupted!");

                trustedCerificates = new X509Certificate2Collection();

                using (MemoryStream zippedMozRootsRawDataStream = new MemoryStream(zippedMozRootsRawData.bytes))
                {
                    using (ZipInputStream zipInputStream = new ZipInputStream(zippedMozRootsRawDataStream))
                    {
                        ZipEntry zipEntry = zipInputStream.GetNextEntry();
                        if (zipEntry != null)
                        {
                            using (StreamReader streamReader = new StreamReader(zipInputStream))
                            {
                                StringBuilder sb = new StringBuilder();
                                bool processing = false;

                                while (true)
                                {
                                    string line = streamReader.ReadLine();
                                    if (line == null)
                                        break;
                                    if (processing)
                                    {
                                        if (line.StartsWith("END"))
                                        {
                                            processing = false;
                                            X509Certificate root = decodeCertificate(sb.ToString());
                                            trustedCerificates.Add(root);

                                            sb = new StringBuilder();
                                            continue;
                                        }
                                        sb.Append(line);
                                    }
                                    else
                                    {
                                        processing = line.StartsWith("CKA_VALUE MULTILINE_OCTAL");
                                    }
                                }
                                streamReader.Close();

                                FunDebug.Log("{0} Root certificates loaded.", trustedCerificates.Count);
                            }
                        }
                        zipInputStream.Close();
                    }
                }
            }
            catch (Exception e)
            {
                FunDebug.LogError("Load certificates file failed. {0}", e.Message);
            }
        }

        public static bool CheckRootCertificate (X509Chain chain)
        {
            if (trustedCerificates == null)
                return false;

            for (int i = chain.ChainElements.Count - 1; i >= 0; i--)
            {
                X509ChainElement chainElement = chain.ChainElements[i];
                foreach (X509Certificate2 trusted in trustedCerificates)
                {
                    if (chainElement.Certificate.Equals(trusted))
                        return true;
                }
            }

            return false;
        }

        static byte[] decodeOctalString (string s)
        {
            string[] pieces = s.Split('\\');
            byte[] data = new byte[pieces.Length - 1];
            for (int i = 1; i < pieces.Length; i++)
            {
                data[i - 1] = (byte)((pieces[i][0] - '0' << 6) + (pieces[i][1] - '0' << 3) + (pieces[i][2] - '0'));
            }
            return data;
        }

        static X509Certificate2 decodeCertificate (string s)
        {
            byte[] rawdata = decodeOctalString(s);
            return new X509Certificate2(rawdata);
        }


        const string kMozillaCertificatesUrl = "http://mxr.mozilla.org/seamonkey/source/security/nss/lib/ckfw/builtins/certdata.txt?raw=1";
        const string kDownloadCertificatesPath = "/Resources/Funapi/MozRoots.bytes";
        const string kResourceCertificatesPath = "Funapi/MozRoots";

        static X509Certificate2Collection trustedCerificates = null;
    }
}
