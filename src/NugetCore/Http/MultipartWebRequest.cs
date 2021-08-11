using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace NuGet
{
    /// <remarks>
    /// Based on the blog post by Travis Illig at http://www.paraesthesia.com/archive/2009/12/16/posting-multipartform-data-using-.net-webrequest.aspx
    /// </remarks>
    public class MultipartWebRequest
    {
        private const string FormDataTemplate = "--{0}\r\nContent-Disposition: form-data; name=\"{1}\"\r\n\r\n{2}\r\n";
        private const string FileTemplate = "--{0}\r\nContent-Disposition: form-data; name=\"{1}\"; filename=\"{2}\"\r\nContent-Type: {3}\r\n\r\n";
        private readonly Dictionary<string, string> _formData;

        private readonly List<PostFileData> _files;

        public MultipartWebRequest()
            : this(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
        }

        public MultipartWebRequest(Dictionary<string, string> formData)
        {
            _formData = formData;
            _files = new List<PostFileData>();
        }

        public void AddFormData(string key, string value)
        {
            _formData.Add(key, value);
        }

        public void AddFile(Func<Stream> fileFactory, string fieldName, long length, string contentType = "application/octet-stream")
        {
            _files.Add(new PostFileData
                        {
                            FileFactory = fileFactory,
                            FieldName = fieldName,
                            ContentType = contentType,
                            ContentLength = length
                        });
        }

        public void CreateMultipartRequest(WebRequest request)
        {
            string boundary = "---------------------------" + DateTime.Now.Ticks.ToString("x", CultureInfo.InvariantCulture);
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            request.ContentLength = CalculateContentLength(boundary);

            using (Stream stream = request.GetRequestStream())
            {
                foreach (var item in _formData)
                {
                    string header = String.Format(CultureInfo.InvariantCulture, FormDataTemplate, boundary, item.Key, item.Value);
                    byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                    stream.Write(headerBytes, 0, headerBytes.Length);
                }

                byte[] newlineBytes = Encoding.UTF8.GetBytes(Environment.NewLine);
                foreach (var file in _files)
                {
                    string header = String.Format(CultureInfo.InvariantCulture, FileTemplate, boundary, file.FieldName, file.FieldName, file.ContentType);
                    byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                    stream.Write(headerBytes, 0, headerBytes.Length);

                    Stream fileStream = file.FileFactory();
                    fileStream.CopyTo(stream, bufferSize: 4 * 1024);
                    fileStream.Close();
                    stream.Write(newlineBytes, 0, newlineBytes.Length);
                }

                string trailer = String.Format(CultureInfo.InvariantCulture, "--{0}--", boundary);
                byte[] trailerBytes = Encoding.UTF8.GetBytes(trailer);
                stream.Write(trailerBytes, 0, trailerBytes.Length);
            }
        }

        private long CalculateContentLength(string boundary)
        {
            long totalContentLength = 0;

            foreach (var item in _formData)
            {
                string header = String.Format(CultureInfo.InvariantCulture, FormDataTemplate, boundary, item.Key, item.Value);
                byte[] headerBytes = Encoding.UTF8.GetBytes(header);

                totalContentLength += headerBytes.Length;
            }

            byte[] newlineBytes = Encoding.UTF8.GetBytes(Environment.NewLine);
            foreach (var file in _files)
            {
                string header = String.Format(CultureInfo.InvariantCulture, FileTemplate, boundary, file.FieldName, file.FieldName, file.ContentType);
                byte[] headerBytes = Encoding.UTF8.GetBytes(header);

                totalContentLength += headerBytes.Length;
                totalContentLength += file.ContentLength;
                totalContentLength += newlineBytes.Length;
            }

            string trailer = String.Format(CultureInfo.InvariantCulture, "--{0}--", boundary);
            byte[] trailerBytes = Encoding.UTF8.GetBytes(trailer);

            totalContentLength += trailerBytes.Length;

            return totalContentLength;
        }

        private sealed class PostFileData
        {
            public Func<Stream> FileFactory { get; set; }

            public string ContentType { get; set; }

            public string FieldName { get; set; }

            public long ContentLength { get; set; }
        }
    }
}