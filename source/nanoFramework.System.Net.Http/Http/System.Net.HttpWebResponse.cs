////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) Microsoft Corporation.  All rights reserved.
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace System.Net
{
    using System;
    using System.Collections;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Diagnostics;

    /// <summary>
    /// Handles retrieval of HTTP Response headers, and handles data reads.
    /// </summary>
    /// <remarks>This class should never be created directly, but rather should
    /// be created by the
    /// <itemref>HttpWebRequest</itemref>.<see cref="System.Net.HttpWebRequest.GetResponse"/>
    /// method.
    /// </remarks>
    public class HttpWebResponse : WebResponse
    {

        /// <summary>
        /// The Web request object that was used for this response.
        /// We need it to access KeepAlive property.
        /// </summary>
        private HttpWebRequest m_httpWebRequest;

        /// <summary>
        /// Response URI generated by the request.
        /// </summary>
        private Uri m_url;

        /// <summary>
        /// response Method gernated by the request
        /// </summary>
        private string m_method;

        /// <summary>
        /// ConnectStream - for reading actual data
        /// </summary>
        private InputNetworkStreamWrapper m_responseStream;

        /// <summary>
        /// Collection of HTTP headers returned by server
        /// </summary>
        private WebHeaderCollection m_httpResponseHeaders;

        /// <summary>
        /// Content Length needed for semantics, -1 if chunked
        /// </summary>
        private long m_contentLength = -1;

        /// <summary>
        /// The HTTP version for the response.
        /// </summary>
        private Version m_version;

        /// <summary>
        /// The status code from the response.
        /// </summary>
        private int m_statusCode;

        /// <summary>
        /// the description of the status returned by the server.
        /// </summary>
        private String m_statusDescription;

        /// <summary>
        /// Retrieves a response header object.
        /// </summary>
        /// <value>A <b>WebHeaderCollection</b> that contains the header
        /// information returned with the response.</value>
        public override WebHeaderCollection Headers
        {
            get
            {
                return m_httpResponseHeaders;
            }
        }

        /// <summary>
        /// Gets the length of the content returned by the request.
        /// </summary>
        /// <remarks>
        /// This property contains the value of the <b>Content-Length</b> header
        /// that is returned with the response.  If the <b>Content-Length</b>
        /// header is not set in the response, this property is set to -1.
        /// </remarks>
        /// <value>The number of bytes returned by the request.  Content length
        /// does not include header information.</value>
        public override long ContentLength
        { get { return m_contentLength; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <value>TBD</value>
        internal long InternalContentLength
        { set { m_contentLength = value; } }

        /// <summary>
        /// Gets the method that is used to encode the body of the response.
        /// </summary>
        /// <remarks>
        /// This property contains the value of the <b>Content-Encoding</b>
        /// header returned with the response; that is, the encoding used for
        /// the response.
        /// </remarks>
        /// <value>A string that describes the method that is used to encode the
        /// body of the response.</value>
        public String ContentEncoding
        {
            get
            {
                return GetResponseHeader(HttpKnownHeaderNames.ContentEncoding);
            }
        }

        /// <summary>
        /// Gets the content type of the response.
        /// </summary>
        /// <value>A string that contains the content type of the response.
        /// </value>
        /// <remarks>
        /// This property contains the value of the <b>Content-Type</b> header
        /// returned with the response.
        /// </remarks>
        public override string ContentType
        {
            get
            {
                return GetResponseHeader(HttpKnownHeaderNames.ContentType);
            }
        }

        /// <summary>
        /// Gets the name of the server that sent the response.
        /// </summary>
        /// <value>A string that contains the name of the server that sent the
        /// response.</value>
        public string Server
        {
            get
            {
                return GetResponseHeader(HttpKnownHeaderNames.Server);
            }
        }

        /// <summary>
        /// Gets the value of the Last-Modified header, which indicates the last
        /// time the document was modified.
        /// </summary>
        /// <value>A <see cref="System.DateTime"/> that contains the date and
        /// time that the contents of the response were modified.</value>
        public DateTime LastModified
        {
            get
            {

                string lastmodHeaderValue =
                    m_httpResponseHeaders[HttpKnownHeaderNames.LastModified];
                if (lastmodHeaderValue == null)
                {
                    return DateTime.UtcNow;
                }

                return HttpProtocolUtils.string2date(lastmodHeaderValue);
            }
        }

        /// <summary>
        /// Gets the status of the HTTP response, as a number.
        /// </summary>
        /// <remarks>
        /// For status code values, see <see cref="System.Net.HttpStatusCode"/>.
        /// </remarks>
        /// <value>One of the <b>HttpStatusCode</b> values.</value>
        public HttpStatusCode StatusCode
        {
            get { return (HttpStatusCode)m_statusCode; }
        }

        /// <summary>
        /// Gets the status description returned with the response.
        /// </summary>
        /// <value>A string that describes the status of the response.</value>
        public string StatusDescription
        {
            get
            {
                return m_statusDescription;
            }
        }

        /// <summary>
        /// Gets the version of the HTTP protocol that is used in the response.
        /// </summary>
        /// <value>A <b>Version</b> that contains the HTTP protocol version of
        /// the response.</value>
        public Version ProtocolVersion
        {
            get { return m_version; }
#if DEBUG
            set { m_version = value; }
#endif
        }

        /// <summary>
        /// Gets the stream used for reading the body of the response from the
        /// server.
        /// </summary>
        /// <returns>A network stream to read body of the message.</returns>
        public override Stream GetResponseStream()
        {
            Stream retVal = m_responseStream.CloneStream();

            m_responseStream.m_dataStart = m_responseStream.m_dataEnd = 0;
            
            return retVal;
        }

        /// <summary>
        /// Sets the response stream.
        /// </summary>
        /// <param name="stream"></param>
        /// <remarks>
        /// Used internally during creation of HttpWebResponse.
        /// </remarks>
        internal void SetResponseStream(InputNetworkStreamWrapper stream)
        {
            m_responseStream = stream;
        }

        /// <summary>
        /// Creates WEB response based on information known just after parsing the status line.
        /// </summary>
        /// <param name="method">Http Verb</param>
        /// <param name="responseUrl">TBD</param>
        /// <param name="data">Response data</param>
        /// <param name="httpWebReq">TBD</param>
        internal HttpWebResponse(string method, Uri responseUrl,
                                  CoreResponseData data, HttpWebRequest httpWebReq)
        {
            m_httpWebRequest = httpWebReq;
            m_method = method;
            m_url = responseUrl;
            m_version = data.m_version;
            m_statusCode = data.m_statusCode;
            m_statusDescription = data.m_statusDescription;

            m_httpResponseHeaders = data.m_headers;

            m_contentLength = data.m_contentLength;
        }

        /// <summary>
        /// Gets the contents of a header that was returned with the response.
        /// </summary>
        /// <param name="headerName">HTTP header to search for matching header on.</param>
        /// <returns>The matched entry, if found.</returns>
        ///
        public string GetResponseHeader(string headerName)
        {
            string headerValue = m_httpResponseHeaders[headerName];
            return ((headerValue == null) ? String.Empty : headerValue);
        }

        /// <summary>
        /// Gets the final Response URI, that includes any
        /// changes that may have transpired from the orginal Request.
        /// </summary>
        /// <value>A <b>Uri</b> that contains the URI of the Internet resource
        /// that responded to the request.</value>
        public override Uri ResponseUri { get { return m_url; } }

        /// <summary>
        /// Gets the method that is used to return the response.
        /// </summary>
        /// <value>A string that contains the HTTP method that is used to return
        /// the response.</value>
        public string Method { get { return m_method; } }

        /// <summary>
        /// Closes a response stream, if present.
        /// </summary>
        /// <param name="disposing">Not used.</param>
        protected override void Dispose(bool disposing)
        {
            if (m_responseStream != null)
            {
                bool closeConnection = true;
                if (m_httpWebRequest.KeepAlive)
                {
                    string connValue = null;

                    // Check if server have sent use "Connection:Close"
                    if (m_httpResponseHeaders != null) connValue = m_httpResponseHeaders[HttpKnownHeaderNames.Connection];

                    // If server had not send this header or value is not "close", then we keep connection.
                    closeConnection = connValue == null || connValue.ToLower() == HttpKnownHeaderValues.close;
                }

                // If it is not in the list - Add it
                if (closeConnection)
                {
                    // Add new socket and port used to connect to the list of sockets.
                    // Save connected socket and Destination IP End Point, so it can be used next time.
                    // But first we need to validate that this socket is already not in the list. We do not want same socket to be twice in the list.

                    HttpWebRequest.RemoveStreamFromPool(m_responseStream);

                    // Closing connection socket.
                    m_responseStream.Dispose();
                }
                else
                {
                    m_responseStream.ReleaseStream();
                }
                
                // Set flag that we already completed work on this stream.
                m_responseStream = null;
            }

            base.Dispose(disposing);
        }
    } // class HttpWebResponse
} // namespace System.Net


