using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Security;
using System.Runtime.Serialization;
using Microsoft.SharePoint.Client;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.tool.xml;
using iTextSharp.tool.xml.pipeline.html;
using iTextSharp.tool.xml.pipeline.css;
using iTextSharp.tool.xml.parser;
using iTextSharp.tool.xml.pipeline.end;
using iTextSharp.tool.xml.html;
using HtmlAgilityPack;

namespace SPListPDFPrint
{

    //public static class FuncSPListPDFPrint
    //{
      //  [FunctionName("FuncSPListPDFPrint")]
      /*
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            // parse query parameter
            string name = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "name", true) == 0)
                .Value;

            if (name == null)
            {
                // Get request body
                dynamic data = await req.Content.ReadAsAsync<object>();
                name = data?.name;
            }

            return name == null
                ? req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a name on the query string or in the request body")
                : req.CreateResponse(HttpStatusCode.OK, "Hello " + name);
        }
    }
    */
    public static class FuncSPListPDFPrint
    {
        static Font _largeFont = new Font(Font.FontFamily.HELVETICA, 18, Font.BOLD, BaseColor.BLACK);
        static Font _standardFont = new Font(Font.FontFamily.HELVETICA, 14, Font.BOLD, BaseColor.BLACK);
        static Font _smallFont = new Font(Font.FontFamily.HELVETICA, 10, Font.NORMAL, BaseColor.BLACK);
        static System.Collections.Generic.Dictionary<string, string> dicStatement = null;

        [FunctionName("FuncSPListPDFPrint")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("1");
            // Parse query string parameter
            string trackNo = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "trackNo", true) == 0)
                .Value;

            string verNo = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "verNo", true) == 0)
                .Value;

            string printLang = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "lang", true) == 0)
                .Value;

            if (string.IsNullOrWhiteSpace(trackNo) || string.IsNullOrWhiteSpace(verNo) || string.IsNullOrWhiteSpace(printLang))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest, "Please pass Tracking number, Version number and Print language on the query string");
                return errorResponse;
            }
            else
            {
                try
                {
                    byte[] bytes = GeneratePDFFile(log, trackNo, verNo, printLang);
                    //log.Info("trackNo:" + trackNo);
                    //log.Info("verNo:" + verNo);
                    //log.Info("Print lang:" + printLang);
                    //var result = req.CreateResponse(HttpStatusCode.OK, dicStatement["Statement Header"] + " - " + dicStatement["Tracking#"] + ".pdf");
                    var result = new HttpResponseMessage(HttpStatusCode.OK);

                    //var temp = req.CreateResponse(HttpStatusCode.OK, "greeting");

                    result.Content = new ByteArrayContent(bytes);
                    result.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                    { FileName = dicStatement["Statement Header"] + " - " + dicStatement["Tracking#"] + ".pdf" };
                    result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                    return result;
                }
                catch (Exception ex)
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError, "ERROR : " + ex.ToString());
                    return errorResponse;
                }
            }

        }

        public static byte[] GeneratePDFFile(TraceWriter log, string trackNo, string verNo, string printLang)
        {
            iTextSharp.text.Document doc = new Document();
            byte[] bytes = null;
            try
            {
                log.Info("2");

                string siteUrl = "https://company.sharepoint.com/teams/nh_SPList";
                ClientContext clientContext = new ClientContext(siteUrl);
                MemoryStream memStream = new MemoryStream();
                SecureString password = new SecureString();

                foreach (char c in "passW0r@1234".ToCharArray()) password.AppendChar(c);
                clientContext.Credentials = new SharePointOnlineCredentials("useraccount@company.onmicrosoft.com", password);

                dicStatement = GetDataFromSharePoint(clientContext, trackNo, verNo, log);

                iTextSharp.text.pdf.PdfWriter writer = iTextSharp.text.pdf.PdfWriter.GetInstance(doc, memStream);
                // Set margins and page size for the document 
                doc.SetMargins(50, 50, 25, 25);
                // There are a huge number of possible page sizes, including such sizes as 
                doc.SetPageSize(new iTextSharp.text.Rectangle(iTextSharp.text.PageSize.A4.Width, iTextSharp.text.PageSize.A4.Height));
                // Add metadata to the document.  This information is visible when viewing the 
                // document properities within Adobe Reader. 
                doc.AddTitle(dicStatement["Statement Header"] + " - " + dicStatement["Tracking#"]);
                //doc.AddCreator("Larsson, Thomas");

                log.Info("3");

                iTextSharp.text.Image logoImage;
                if (dicStatement["Document Template"] == "Global General Template")
                {
                    logoImage = GetSignature(clientContext, "companylogo.png");
                    logoImage.Alignment = Element.ALIGN_LEFT;
                    logoImage.ScalePercent(80);
                }
                else
                {
                    logoImage = GetSignature(clientContext, "SPListtemplatelogo.png");
                    logoImage.Alignment = Element.ALIGN_LEFT;
                    logoImage.ScalePercent(80);
                }

                iTextSharp.text.Image iTextLogoImage = null;
                var getStatus = Convert.ToString(dicStatement["Status"]);

                if (getStatus != "Final")
                {
                    iTextLogoImage = GetSignature(clientContext, "DraftLogo.png");
                    if (iTextLogoImage != null)
                    {
                        iTextLogoImage.ScaleAbsolute(426, 371);
                        iTextLogoImage.SetAbsolutePosition((PageSize.A4.Width - iTextLogoImage.ScaledWidth) / 2, (PageSize.A4.Height - iTextLogoImage.ScaledHeight) / 2);

                    }

                }

                // Open the document for writing content 
                writer.PageEvent = new PDFHeaderFooter(dicStatement, logoImage, iTextLogoImage);
                doc.Open();

                doc.Add(logoImage);

                log.Info("4");

                // Add pages to the document 
                AddPageWithBasicFormatting(writer, doc, clientContext, log, printLang);

                //log.Info(chinesefontpath);

                //log.Info(new StringReader(newPhrase));

                doc.Close();
                bytes = memStream.ToArray();
                memStream.Close();

                // Upload file to SharePoint document library

                FileCreationInformation uploadFile = new FileCreationInformation();
                string DocTitle = dicStatement["Statement Header"].Replace(@"/", " ");
                DocTitle = DocTitle.Replace(@"&", " ");
                uploadFile.Url = DocTitle + " - " + dicStatement["Tracking#"] + ".pdf";
                uploadFile.Overwrite = true;
                uploadFile.Content = bytes;

                Microsoft.SharePoint.Client.List spList = clientContext.Web.Lists.GetByTitle("StatementPDFs");
                Microsoft.SharePoint.Client.File addedFile = spList.RootFolder.Files.Add(uploadFile);
                clientContext.Load(addedFile);
                clientContext.ExecuteQuery();

                //System.IO.File.WriteAllBytes(System.IO.Directory.GetCurrentDirectory() + "\\" + dicStatement["Statement Header"] + " - " + dicStatement["Tracking#"] + ".pdf", bytes);

                Microsoft.SharePoint.Client.ListItem item = addedFile.ListItemAllFields;
                item["Title"] = dicStatement["Id"];
                item.Update();
                clientContext.Load(item);
                clientContext.ExecuteQuery();

                log.Info("5");
            }
            catch (iTextSharp.text.DocumentException dex)
            {
                // Handle iTextSharp errors
                Console.WriteLine(dex.ToString());                
            }
            finally
            {
                // Clean up 
                doc.Close();
                doc = null;
            }
            return bytes;
        }


        private static void AddPageWithBasicFormatting(iTextSharp.text.pdf.PdfWriter writer, iTextSharp.text.Document doc, ClientContext clientContext, TraceWriter log, string printLang)
        {

            //Adds Chinese Font

            //Use BaseFont to load unicode fonts like Simplified Chinese font
            string arialfontpath = @"D:\home\site\wwwroot\arial.ttf";
            string chinesefontpath = @"D:\home\site\wwwroot\mssong.ttf";
            //string arialfontpath = @"C:\Users\py6142\OneDrive - company\02 Project\00 Solution Request\20190501 SPList Phase 2\SPListfunction\HttpTriggerCSharp1\arial.ttf";
            //string chinesefontpath = @"C:\Users\py6142\OneDrive - company\02 Project\00 Solution Request\20190501 SPList Phase 2\SPListfunction\HttpTriggerCSharp1\mssong.ttf";

            FontSelector selector = new FontSelector();
            Font f1 = FontFactory.GetFont(arialfontpath, 17, Font.BOLD);
            Font f2 = FontFactory.GetFont(chinesefontpath, BaseFont.IDENTITY_H, BaseFont.NOT_EMBEDDED);
            f2.Size = 16;

            selector.AddFont(f1);
            selector.AddFont(f2);

            var fontProv1 = new XMLWorkerFontProvider();

            var charset = Encoding.UTF8;

            if (printLang.Contains("English") == true)
            {
                fontProv1.Register(arialfontpath, "SimSun");
                log.Info("English");
            }
            else if (printLang.Contains("Chinese") == true)
            {
                fontProv1.Register(chinesefontpath, "SimSun");
                log.Info("Chinese");
            }
            else
            {
                fontProv1.Register(arialfontpath, "SimSun");
                log.Info("Else");
            }


            HtmlPipelineContext htmlContext = new HtmlPipelineContext(new CssAppliersImpl(fontProv1));

            var tagProcessors1 = (DefaultTagProcessorFactory)Tags.GetHtmlTagProcessorFactory();
            tagProcessors1.RemoveProcessor(HTML.Tag.IMG); // remove the default processor
            tagProcessors1.AddProcessor(HTML.Tag.IMG, new CustomImageTagProcessor()); // use our new processor

            htmlContext.SetAcceptUnknown(true).AutoBookmark(true).SetTagFactory(tagProcessors1);

            //create a cssresolver to apply css
            ICSSResolver cssResolver = XMLWorkerHelper.GetInstance().GetDefaultCssResolver(true);

            cssResolver.AddCss("p{margin: 16px 0 16px 0;}", "utf-8", true);
            cssResolver.AddCss("sup{vertical-align: super;font-size: smaller;}", "utf-8", true);
            cssResolver.AddCss("sup a{text-decoration: none;}", "utf-8", true);
            cssResolver.AddCss("ul{margin: 10px 0 10px 0;}", "utf-8", true);
            cssResolver.AddCss("ul li{font-size: 10px;}", "utf-8", true);
            cssResolver.AddCss("#ptintent p{font-size: 16px}", "utf-8", true);
            cssResolver.AddCss("table{border-width: 0}", "utf-8", true);
            cssResolver.AddCss("table{border-collapse: collapse;border-color: #808080;}", "utf-8", true);
            cssResolver.AddCss("p,div,span,sup,td,th{font-family: SimSun,Arial;font-size: 16px;}", "utf-8", true);
            cssResolver.AddCss(".pbPrint{page-break-after: always;}", "utf-8", true);

            //Create and attach pipline, without pipline parser will not work on css
            IPipeline pipeline = new CssResolverPipeline(cssResolver, new HtmlPipeline(htmlContext, new PdfWriterPipeline(doc, writer)));

            //Create XMLWorker and attach a parser to it
            XMLWorker worker = new XMLWorker(pipeline, true);
            XMLParser xmlParser = new XMLParser(true, worker, charset);

            // Write page content.  Note the use of fonts and alignment attributes.

            AddParagraph(doc, iTextSharp.text.Element.ALIGN_CENTER, _largeFont, new Chunk("\n"));
            log.Info("creating space");


            var TempType = Convert.ToString(dicStatement["Document Template"]);
            log.Info(TempType);
            if (TempType == "Global Product Template")
            {

                HtmlDocument htmlDoc = new HtmlDocument();
                htmlDoc.OptionWriteEmptyNodes = true;
                htmlDoc.OptionAutoCloseOnEnd = true;

                htmlDoc.LoadHtml(dicStatement["Product Range"]);
                var htmlNode = htmlDoc.DocumentNode;
                var hasChilds = htmlNode.HasChildNodes;

                if (hasChilds == true)
                {
                    if (htmlNode.FirstChild.InnerText != "")
                    {
                        var hasSpan = htmlNode.SelectNodes("//span");
                        if (hasSpan != null)
                        {
                            foreach (var getspan in hasSpan)
                            {
                                var styleTd = getspan.GetAttributeValue("style", null);

                                if (styleTd != null)
                                {
                                    if (styleTd.Contains("font-family"))
                                    {
                                        var newstyleTd = styleTd.Replace("font-family", "font");
                                        getspan.SetAttributeValue("style", newstyleTd);
                                    }
                                }

                            }
                        }
                        string newhtmlNode = htmlNode.InnerHtml;
                        xmlParser.Parse(new StringReader(newhtmlNode));
                    }
                }
                else
                {
                    string newhtmlNode = htmlNode.InnerHtml;
                    xmlParser.Parse(new StringReader(newhtmlNode));
                }

                HtmlDocument htmlDoc1 = new HtmlDocument();
                htmlDoc1.OptionWriteEmptyNodes = true;
                htmlDoc1.OptionAutoCloseOnEnd = true;

                htmlDoc1.LoadHtml(dicStatement["Production Country"]);
                var htmlNode1 = htmlDoc1.DocumentNode;
                var hasChilds1 = htmlNode1.HasChildNodes;

                if (hasChilds1 == true)
                {
                    if (htmlNode1.FirstChild.InnerText != "")
                    {
                        var hasSpan1 = htmlNode1.SelectNodes("//span");
                        if (hasSpan1 != null)
                        {
                            foreach (var getspan1 in hasSpan1)
                            {
                                var styleTd = getspan1.GetAttributeValue("style", null);

                                if (styleTd != null)
                                {
                                    if (styleTd.Contains("font-family"))
                                    {
                                        var newstyleTd = styleTd.Replace("font-family", "font");
                                        getspan1.SetAttributeValue("style", newstyleTd);
                                    }
                                }

                            }
                        }
                        string newhtmlNode1 = htmlNode1.InnerHtml;
                        xmlParser.Parse(new StringReader(newhtmlNode1));
                    }
                }
                else
                {
                    string newhtmlNode1 = htmlNode1.InnerHtml;
                    xmlParser.Parse(new StringReader(newhtmlNode1));
                }

            }
            log.Info("global product template");
            AddParagraph(doc, iTextSharp.text.Element.ALIGN_CENTER, _largeFont, new Chunk("\n"));

            AddParagraphwithFont(doc, iTextSharp.text.Element.ALIGN_CENTER, selector, dicStatement["Statement Header"]);
            log.Info("printing header");
            //AddParagraph(doc, iTextSharp.text.Element.ALIGN_CENTER, _standardFont, new Chunk(dicStatement["Statement Header"]));
            AddParagraph(doc, iTextSharp.text.Element.ALIGN_CENTER, _largeFont, new Chunk("\n"));

            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(dicStatement["Intent"]);
            document.OptionWriteEmptyNodes = true;
            SetAlignment(document.DocumentNode);

            var strHtml = document.DocumentNode.InnerHtml;

            using (var sr = new StringReader(dicStatement["Intent"]))
            {
                //xmlParser.Parse(str);

                //Getting all image tag's src attribute from the html
                List<Uri> links = FetchLinksFromSource(strHtml, log);

                string htmlsource = strHtml;
                foreach (var link in links)
                {
                    var url = link.AbsoluteUri;
                    if (GetBinaryByURL(clientContext, url) != "")
                    {

                        var base64src = "data:image/jpeg;base64," + GetBinaryByURL(clientContext, url);

                        htmlsource = htmlsource.Replace(url, base64src);

                    }

                }

                // List<string> allTables = FindTablesFromSource(strHtml, log);
                //     foreach (var table in allTables)
                //     {
                //         var oldStyle = table;
                //         var newStyle = table.Replace("width","widt");
                //         htmlsource = htmlsource.Replace(oldStyle, newStyle);
                //     }

                // var testhtmlsource = htmlsource;
                htmlsource = htmlsource.Replace("<sup>&copy;</sup>", "<sup><font face='Symbol'>&#211;</font></sup>");
                htmlsource = htmlsource.Replace("&copy;", "<font face='Symbol'>&#211;</font>");
                htmlsource = htmlsource.Replace("<sup>&reg;</sup>", "<sup><font face='Symbol'>&#210;</font></sup>");
                var newhtmlsource = new StringReader(htmlsource);

                xmlParser.Parse(newhtmlsource);
                log.Info("parsing intent");

            }


            AddParagraph(doc, iTextSharp.text.Element.ALIGN_CENTER, _largeFont, new Chunk("\n"));

            using (var sr = new StringReader(dicStatement["Signee Salutaion"]))
            {

                HtmlDocument salut = new HtmlDocument();
                salut.OptionWriteEmptyNodes = true;
                salut.OptionAutoCloseOnEnd = true;

                salut.LoadHtml(dicStatement["Signee Salutaion"]);
                var hasNodes = salut.DocumentNode;

                var hasChilds1 = hasNodes.HasChildNodes;
                if (hasChilds1 == true)
                {
                    if (hasNodes.FirstChild.InnerText != "")
                    {
                        string salutString = salut.DocumentNode.OuterHtml;
                        XMLWorkerHelper.GetInstance().ParseXHtml(writer, doc, new StringReader(salutString));
                    }
                }
                else
                {
                    string salutString = salut.DocumentNode.OuterHtml;
                    XMLWorkerHelper.GetInstance().ParseXHtml(writer, doc, new StringReader(salutString));
                }

            }


            //Adding Signature Image

            var getStatus = Convert.ToString(dicStatement["Status"]);

            if (getStatus == "Final")
            {
                iTextSharp.text.Image iTextImage = GetSignature(clientContext, "");
                if (iTextImage != null)
                {
                    iTextImage.ScaleAbsolute(135f, 53f);
                    doc.Add(iTextImage);
                }
            }

            using (var sr = new StringReader(dicStatement["Display Name"]))
            {
                HtmlDocument dispName = new HtmlDocument();
                dispName.OptionWriteEmptyNodes = true;
                dispName.OptionAutoCloseOnEnd = true;

                dispName.LoadHtml(dicStatement["Display Name"]);
                var hasNodes = dispName.DocumentNode;

                var hasChilds1 = hasNodes.HasChildNodes;
                if (hasChilds1 == true)
                {
                    if (hasNodes.FirstChild.InnerText != "")
                    {
                        string salutString = dispName.DocumentNode.OuterHtml;
                        XMLWorkerHelper.GetInstance().ParseXHtml(writer, doc, new StringReader(salutString));
                    }
                }
                else
                {
                    string salutString = dispName.DocumentNode.OuterHtml;
                    XMLWorkerHelper.GetInstance().ParseXHtml(writer, doc, new StringReader(salutString));
                }
            }

            using (var sr = new StringReader(dicStatement["Designation"]))
            {
                HtmlDocument desig = new HtmlDocument();
                desig.OptionWriteEmptyNodes = true;
                desig.OptionAutoCloseOnEnd = true;

                desig.LoadHtml(dicStatement["Designation"]);
                var hasNodes = desig.DocumentNode;

                var hasChilds1 = hasNodes.HasChildNodes;
                if (hasChilds1 == true)
                {
                    if (hasNodes.FirstChild.InnerText != "")
                    {
                        string salutString = desig.DocumentNode.OuterHtml;
                        XMLWorkerHelper.GetInstance().ParseXHtml(writer, doc, new StringReader(salutString));
                    }
                }
                else
                {
                    string salutString = desig.DocumentNode.OuterHtml;
                    XMLWorkerHelper.GetInstance().ParseXHtml(writer, doc, new StringReader(salutString));
                }

            }

            using (var sr = new StringReader(dicStatement["Division/Business"]))
            {

                HtmlDocument division = new HtmlDocument();
                division.OptionWriteEmptyNodes = true;
                division.OptionAutoCloseOnEnd = true;

                division.LoadHtml(dicStatement["Division/Business"]);
                var hasNodes = division.DocumentNode;

                var hasChilds1 = hasNodes.HasChildNodes;
                if (hasChilds1 == true)
                {
                    if (hasNodes.FirstChild.InnerText != "")
                    {
                        string salutString = division.DocumentNode.OuterHtml;
                        XMLWorkerHelper.GetInstance().ParseXHtml(writer, doc, new StringReader(salutString));
                    }
                }
                else
                {
                    string salutString = division.DocumentNode.OuterHtml;
                    XMLWorkerHelper.GetInstance().ParseXHtml(writer, doc, new StringReader(salutString));
                }
            }

            AddParagraph(doc, iTextSharp.text.Element.ALIGN_CENTER, _largeFont, new Chunk("\n\n\n"));

            using (var sr = new StringReader(dicStatement["Footnotes"]))
            {
                HtmlDocument footnotes = new HtmlDocument();
                footnotes.OptionWriteEmptyNodes = true;
                footnotes.OptionAutoCloseOnEnd = true;

                footnotes.LoadHtml(dicStatement["Footnotes"]);
                var hasNodes = footnotes.DocumentNode;

                var hasChilds1 = hasNodes.HasChildNodes;
                if (hasChilds1 == true)
                {
                    if (hasNodes.InnerText != "" && hasNodes.InnerText != "Add footnotes here......")
                    {
                        foreach (var elem in hasNodes.Elements("div"))
                        {
                            foreach (HtmlNode getPara in elem.Descendants())
                            {
                                if (getPara.Name == "p")
                                {
                                    getPara.SetAttributeValue("style", "font-family:Arial; font-size:8pt;");
                                }
                            }
                        }
                        string fnotesString = footnotes.DocumentNode.OuterHtml;
                        XMLWorkerHelper.GetInstance().ParseXHtml(writer, doc, new StringReader(fnotesString));
                    }
                }
            }
            FontSelector disclaimerFontSelector = new FontSelector();
            disclaimerFontSelector.AddFont(FontFactory.GetFont(arialfontpath, 6, Font.ITALIC));
            if (dicStatement["BoolDisclaimer"] == "True")
            {
                AddParagraphwithFont(doc, iTextSharp.text.Element.ALIGN_BOTTOM, disclaimerFontSelector, "DISCLAIMER");
                AddParagraphwithFont(doc, iTextSharp.text.Element.ALIGN_BOTTOM, disclaimerFontSelector, dicStatement["Disclaimer"]);

            }


        }

        public static void SetAlignment(HtmlNode node)
        {

            var style = "";
            var style1 = "";
            var style2 = "";
            var styleTable = "";
            int imgWidth;
            int defaultImgWidth = 650;

            foreach (var item in node.Elements("table"))
            {

                item.SetAttributeValue("style", "width:100%");
                styleTable = item.GetAttributeValue("style", null);

                if (styleTable != null)
                {
                    if (styleTable.Contains("margin-left: auto; margin-right: auto"))
                    {
                        item.Attributes.Add("align", "Center");
                    }
                    else if (styleTable.Contains("float: right"))
                    {
                        item.Attributes.Add("align", "right");
                    }

                }

            }

            foreach (HtmlNode element in node.Elements("p"))
            {

                var pagebreakNode = element.FirstChild;

                if (pagebreakNode.NodeType == HtmlNodeType.Comment)
                {
                    pagebreakNode.ParentNode.AddClass("pbPrint");
                }

                foreach (HtmlNode getChilds in element.Descendants())
                {
                    if (getChilds.Name == "img")
                    {

                        style = getChilds.GetAttributeValue("style", null);

                        if (style != null)
                        {
                            if (style.Contains("margin-left: auto; margin-right: auto"))
                            {
                                element.Attributes.Add("align", "Center");
                            }
                            else if (style.Contains("float: right"))
                            {
                                element.Attributes.Add("align", "right");
                            }
                        }

                        style1 = getChilds.GetAttributeValue("width", null);
                        style2 = getChilds.GetAttributeValue("height", null);


                        if (style1 != null)
                        {
                            imgWidth = Int32.Parse(style1);
                            if (imgWidth > defaultImgWidth)
                            {
                                getChilds.SetAttributeValue("width", "650");
                                getChilds.Attributes.Remove("height");
                            }
                        }
                    }
                    else
                    {
                        foreach (HtmlNode getimgTag in getChilds.Elements("img"))
                        {


                            if (getimgTag != null)
                            {

                                style = getimgTag.GetAttributeValue("style", null);

                                if (style != null)
                                {
                                    if (style.Contains("margin-left: auto; margin-right: auto"))
                                    {
                                        element.Attributes.Add("align", "Center");
                                    }
                                    else if (style.Contains("float: right"))
                                    {
                                        element.Attributes.Add("align", "right");
                                    }
                                }

                                style1 = getimgTag.GetAttributeValue("width", null);
                                style2 = getimgTag.GetAttributeValue("height", null);


                                if (style1 != null)
                                {
                                    imgWidth = Int32.Parse(style1);
                                    if (imgWidth > defaultImgWidth)
                                    {
                                        getimgTag.SetAttributeValue("width", "650");
                                        getimgTag.Attributes.Remove("height");
                                    }
                                }
                            }
                        }
                        foreach (HtmlNode gettableTag in getChilds.Elements("table"))
                        {
                            var tableStyle = gettableTag.GetAttributeValue("style", null);

                            if (tableStyle != null)
                            {
                                if (tableStyle.Contains("margin-left: auto; margin-right: auto"))
                                {
                                    gettableTag.Attributes.Add("align", "Center");
                                }
                                else if (tableStyle.Contains("float: right"))
                                {
                                    gettableTag.Attributes.Add("align", "right");
                                }
                            }

                        }
                    }

                }

                var hasSpan = element.SelectNodes("//span");
                if (hasSpan != null)
                {
                    foreach (var td in hasSpan)
                    {
                        var styleTd = td.GetAttributeValue("style", null);

                        if (styleTd != null)
                        {
                            if (styleTd.Contains("font-family"))
                            {
                                var newstyleTd = styleTd.Replace("font-family", "font");
                                td.SetAttributeValue("style", newstyleTd);
                            }
                        }

                    }
                }

            }

        }

        private static List<Uri> FetchLinksFromSource(string htmlSource, TraceWriter log)
        {
            List<Uri> links = new List<Uri>();
            string regexImgSrc = @"<img[^>]*?src\s*=\s*[""']?([^'"" >]+?)[ '""][^>]*?>";
            MatchCollection matchesImgSrc = Regex.Matches(htmlSource, regexImgSrc, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in matchesImgSrc)
            {
                string href = m.Groups[1].Value;

                links.Add(new Uri(href));
            }
            return links;
        }

        private static List<string> FindTablesFromSource(string htmlSource1, TraceWriter log)
        {

            string table_pattern = "<table.*?>(.*?)</table>";

            MatchCollection table_matches = Regex.Matches(htmlSource1, table_pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            List<string> tableContents = new List<string>();

            foreach (Match match in table_matches)
            {
                tableContents.Add(match.Value);
            }


            return tableContents;
        }

        private static void AddParagraph(Document doc, int alignment, iTextSharp.text.Font font, iTextSharp.text.IElement content)
        {
            Paragraph paragraph = new Paragraph();
            paragraph.SetLeading(0f, 1.2f);
            paragraph.Alignment = alignment;
            paragraph.Font = font;
            paragraph.Add(content);
            doc.Add(paragraph);
        }

        private static void AddParagraphwithFont(Document doc1, int alignment1, FontSelector fonts, String content1)
        {
            Paragraph paragraph = new Paragraph(fonts.Process(content1));
            paragraph.SetLeading(0f, 1.2f);
            paragraph.Alignment = alignment1;
            doc1.Add(paragraph);
        }

        private static System.Collections.Generic.Dictionary<string, string> GetDataFromSharePoint(ClientContext clientContext, string trackNo, string verNo, TraceWriter log)
        {
            System.Collections.Generic.Dictionary<string, string> dicStatementInternal = new System.Collections.Generic.Dictionary<string, string>();

            Microsoft.SharePoint.Client.List oList = clientContext.Web.Lists.GetByTitle("Statements");
            clientContext.Load(oList);
            clientContext.ExecuteQuery();

            CamlQuery oCamlQuery = new CamlQuery();
            //oCamlQuery.ViewXml = "<View Scope='RecursiveAll'><Query><Where><Eq><FieldRef Name='Title' /><Value Type='Text'>" + trackNo + "</Value></Eq></Where></Query></View>";
            oCamlQuery.ViewXml = "<View Scope='RecursiveAll'><Query><Where><And><Eq><FieldRef Name='Title' /><Value Type='Text'>" + trackNo + "</Value></Eq><Eq><FieldRef Name='Issue_x0023_' /><Value Type='Number'>" + verNo + "</Value></Eq></And></Where></Query></View>";

            ListItemCollection collListItem = oList.GetItems(oCamlQuery);
            clientContext.Load(collListItem);
            clientContext.ExecuteQuery();

            var docType = Convert.ToString(collListItem[0]["Document_x0020_Template"]);
            string oldvalue = "src=\"Attachments";
            string newvalue = "src=\"https://company.sharepoint.com/teams/nh_SPList/Lists/Internal%20Documents/Attachments";
            string oldheader = Convert.ToString(collListItem[0]["Document_x0020_Title"]);
            Regex reg = new Regex("[<>:\"|?*]");
            string newheader = reg.Replace(oldheader, string.Empty);

            if (docType == "Global General Template")
            {
                string oldintent = Convert.ToString(collListItem[0]["Intent"]);
                string newintent = oldintent.Replace(oldvalue, newvalue);


                dicStatementInternal.Add("Intent", newintent);
                dicStatementInternal.Add("Id", Convert.ToString(collListItem[0]["ID"]));
                dicStatementInternal.Add("Status", Convert.ToString(collListItem[0]["Status"]));
                dicStatementInternal.Add("Tracking#", Convert.ToString(collListItem[0]["Title"]));
                dicStatementInternal.Add("Document Template", Convert.ToString(collListItem[0]["Document_x0020_Template"]));
                dicStatementInternal.Add("Version#", Convert.ToString(collListItem[0]["Issue_x0023_"]));
                dicStatementInternal.Add("Version Date", Convert.ToString(collListItem[0]["Issue_x0020_Date"]));
                dicStatementInternal.Add("Last Reviewed", Convert.ToString(collListItem[0]["Last_x0020_Reviewed"]));
                //dicStatementInternal.Add("Statement Header", Convert.ToString(collListItem[0]["Document_x0020_Title"]));
                dicStatementInternal.Add("Statement Header", newheader);
                dicStatementInternal.Add("City Address", Convert.ToString(collListItem[0]["City_x0020_Address"]));
                dicStatementInternal.Add("CVR", Convert.ToString(collListItem[0]["GT_x0020_CVR"]));
                dicStatementInternal.Add("Legal Entity", Convert.ToString(collListItem[0]["Legal_x0020_Entity_x0020_Name"]));
                dicStatementInternal.Add("Street Address", Convert.ToString(collListItem[0]["Street_x0020_Address"]));
                dicStatementInternal.Add("Telephone", Convert.ToString(collListItem[0]["Telephone"]));
                dicStatementInternal.Add("Signee Salutaion", Convert.ToString(collListItem[0]["Salutation"]));
                dicStatementInternal.Add("BoolDisclaimer", Convert.ToString(collListItem[0]["Disclaimer"]));
                dicStatementInternal.Add("Disclaimer", Convert.ToString(collListItem[0]["DisclaimerContent"]));
                if (collListItem[0]["FinalApproverSign"] != null)
                {
                    dicStatementInternal.Add("Sign Image", Convert.ToString(((FieldUrlValue)(collListItem[0]["FinalApproverSign"])).Url));
                }
                else
                {
                    dicStatementInternal.Add("Sign Image", "");
                }
                dicStatementInternal.Add("Display Name", Convert.ToString(collListItem[0]["FinalApproverDisplayName"]));
                dicStatementInternal.Add("Designation", Convert.ToString(collListItem[0]["FinalApproverDesignation"]));
                dicStatementInternal.Add("Division/Business", Convert.ToString(collListItem[0]["Business_x002f_Division"]));
                dicStatementInternal.Add("Footnotes", Convert.ToString(collListItem[0]["Footnotes"]));

            }
            else
            {
                string oldintent = Convert.ToString(collListItem[0]["PT_x0020_Intent"]);
                string newintent = oldintent.Replace(oldvalue, newvalue);
                dicStatementInternal.Add("Id", Convert.ToString(collListItem[0]["ID"]));
                dicStatementInternal.Add("Intent", newintent);
                dicStatementInternal.Add("Status", Convert.ToString(collListItem[0]["Status"]));
                dicStatementInternal.Add("Tracking#", Convert.ToString(collListItem[0]["Title"]));
                dicStatementInternal.Add("Document Template", Convert.ToString(collListItem[0]["Document_x0020_Template"]));
                dicStatementInternal.Add("Version#", Convert.ToString(collListItem[0]["Issue_x0023_"]));
                dicStatementInternal.Add("Version Date", Convert.ToString(collListItem[0]["Issue_x0020_Date"]));
                dicStatementInternal.Add("Last Reviewed", Convert.ToString(collListItem[0]["Last_x0020_Reviewed"]));
                //dicStatementInternal.Add("Statement Header", Convert.ToString(collListItem[0]["Document_x0020_Title"]));
                dicStatementInternal.Add("Statement Header", newheader);
                dicStatementInternal.Add("City Address", Convert.ToString(collListItem[0]["PT_x0020_City_x0020_Address"]));
                dicStatementInternal.Add("CVR", Convert.ToString(collListItem[0]["PT_x0020_CVR"]));
                dicStatementInternal.Add("Legal Entity", Convert.ToString(collListItem[0]["PT_x0020_Legal_x0020_Entity_x002"]));
                dicStatementInternal.Add("Street Address", Convert.ToString(collListItem[0]["PT_x0020_Street_x0020_Address"]));
                dicStatementInternal.Add("Telephone", Convert.ToString(collListItem[0]["TelephonePT"]));
                dicStatementInternal.Add("Signee Salutaion", Convert.ToString(collListItem[0]["Salutation"]));
                dicStatementInternal.Add("BoolDisclaimer", Convert.ToString(collListItem[0]["Disclaimer"]));
                dicStatementInternal.Add("Disclaimer", Convert.ToString(collListItem[0]["DisclaimerContent"]));
                if (collListItem[0]["FinalApproverSign"] != null)
                {
                    dicStatementInternal.Add("Sign Image", Convert.ToString(((FieldUrlValue)(collListItem[0]["FinalApproverSign"])).Url));
                }
                else
                {
                    dicStatementInternal.Add("Sign Image", "");
                }
                dicStatementInternal.Add("Display Name", Convert.ToString(collListItem[0]["FinalApproverDisplayName"]));
                dicStatementInternal.Add("Designation", Convert.ToString(collListItem[0]["FinalApproverDesignation"]));
                dicStatementInternal.Add("Division/Business", Convert.ToString(collListItem[0]["Business_x002f_Division"]));
                dicStatementInternal.Add("Product Range", Convert.ToString(collListItem[0]["ProductRange"]));
                dicStatementInternal.Add("Production Country", Convert.ToString(collListItem[0]["Production_x0020_Country"]));
                dicStatementInternal.Add("Footnotes", Convert.ToString(collListItem[0]["Footnotes"]));
            }

            return dicStatementInternal;
        }

        private static iTextSharp.text.Image GetSignature(ClientContext clientContext, string logoName)
        {
            iTextSharp.text.Image iTextImage = null;
            Microsoft.SharePoint.Client.List oList = null;
            ListItemCollection collListItem = null;
            Microsoft.SharePoint.Client.ListItem listItem = null;
            Microsoft.SharePoint.Client.File file = null;
            CamlQuery oCamlQuery = new CamlQuery();
            string[] signature = null;
            string signatureName = null;

            if (logoName.ToLower().Contains("companylogo.png") || logoName.ToLower().Contains("SPListtemplatelogo.png") || logoName.ToLower().Contains("draftlogo.png"))
            {
                oList = clientContext.Web.Lists.GetByTitle("Style Library");
                signatureName = logoName;

                oCamlQuery.ViewXml = "<View Scope='RecursiveAll'><Query><Where><Contains><FieldRef Name='FileLeafRef' /><Value Type='File'>" + signatureName + "</Value></Contains></Where></Query></View>";
            }
            else
            {
                oList = clientContext.Web.Lists.GetByTitle("Final Approvers Signature");

                var getImgSRC = dicStatement["Sign Image"];
                if (getImgSRC.IndexOf("Lightbox.aspx", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string getEncodedURL = getImgSRC.Split('=')[1];
                    var decodeURL = System.Net.WebUtility.UrlDecode(getEncodedURL);
                    signature = decodeURL.Split('/');
                    if (signature.Length > 0)
                        signatureName = signature[signature.Length - 1];
                }
                else
                {
                    signature = getImgSRC.Split('/');
                    if (signature.Length > 0)
                        signatureName = System.Net.WebUtility.UrlDecode(signature[signature.Length - 1]);
                }

                oCamlQuery.ViewXml = "<View><Query><Where><Contains><FieldRef Name='FileLeafRef' /><Value Type='File'>" + signatureName + "</Value></Contains></Where></Query></View>";
            }

            collListItem = oList.GetItems(oCamlQuery);
            clientContext.Load(oList);
            clientContext.Load(collListItem);
            clientContext.ExecuteQuery();

            if (collListItem.Count > 0)
            {
                listItem = collListItem[0];
                file = listItem.File;

                clientContext.Load(listItem);
                clientContext.Load(file);
                clientContext.ExecuteQuery();

                if (file.Name.ToLower().Contains(signatureName.ToLower()))
                {
                    FileInformation fileInformation = Microsoft.SharePoint.Client.File.OpenBinaryDirect(clientContext, listItem.File.ServerRelativeUrl);
                    iTextImage = iTextSharp.text.Image.GetInstance(fileInformation.Stream);
                }
            }

            return iTextImage;
        }

        private static string GetBinaryByURL(ClientContext clientContext, string URL)
        {
            string b64String = "";

            try
            {
                Uri filename = new Uri(URL);
                var file = clientContext.Web.GetFileByServerRelativeUrl(filename.AbsolutePath);
                clientContext.Load(file);
                clientContext.ExecuteQuery();
                if (file != null)
                {

                    // FileInformation fileInformation = Microsoft.SharePoint.Client.File.OpenBinaryDirect(ctx1, listItem.File.ServerRelativeUrl);
                    //iTextImage = iTextSharp.text.Image.GetInstance(fileInformation.Stream);

                    ClientResult<Stream> streamResult = file.OpenBinaryStream();
                    clientContext.ExecuteQuery();
                    using (System.IO.MemoryStream mStream = new System.IO.MemoryStream())
                    {
                        if (streamResult != null)
                        {
                            streamResult.Value.CopyTo(mStream);
                            byte[] imageArray = mStream.ToArray();
                            b64String = Convert.ToBase64String(imageArray);
                        }
                    }



                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            return b64String;
        }
    }//class closed ["Function1"]

    [DataContract(Name = "PDFHeaderFooter", Namespace = "http://functions")]
    public class PDFHeaderFooter : PdfPageEventHelper
    {
        iTextSharp.text.Image logoImage = null;
        iTextSharp.text.Image draftLogo = null;
        static System.Collections.Generic.Dictionary<string, string> dicStatement = null;
        static int iPageNumber;

        public PDFHeaderFooter(System.Collections.Generic.Dictionary<string, string> dicStatementParam, iTextSharp.text.Image image, iTextSharp.text.Image image1)
        {
            dicStatement = dicStatementParam;
            logoImage = image;
            draftLogo = image1;

            iPageNumber = 0;
        }

        // write on top of document (For Header)
        public override void OnOpenDocument(PdfWriter writer, Document document)
        {
            base.OnOpenDocument(writer, document);

            //Adds Chinese Font

            //Use BaseFont to load unicode fonts like Simplified Chinese font
            string arialfontpath1 = @"D:\home\site\wwwroot\arial.ttf";
            string chinesefontpath1 = @"D:\home\site\wwwroot\mssong.ttf";

            //"simsun.ttf" file was downloaded from web and placed in the folder
            // BaseFont bf = BaseFont.CreateFont(chinesefontpath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
            // Font fontContent = new Font(bf, 11);

            FontSelector selector1 = new FontSelector();
            Font f1 = FontFactory.GetFont(arialfontpath1);
            f1.Size = 8;
            Font f2 = FontFactory.GetFont(chinesefontpath1, BaseFont.IDENTITY_H, BaseFont.NOT_EMBEDDED);
            f2.Size = 8;

            selector1.AddFont(f1);
            selector1.AddFont(f2);

            float[] myColWidth = new float[] { 100, 15, 380 };

            PdfPTable mainTable = new PdfPTable(3);
            mainTable.SpacingAfter = 1F;
            mainTable.SetTotalWidth(myColWidth);

            PdfPCell cellLOGO = new PdfPCell();
            cellLOGO.Border = 0;
            //cellLOGO.Image = logoImage;
            mainTable.AddCell(cellLOGO);

            PdfPCell cellMiddle = new PdfPCell(new Phrase(""));
            cellMiddle.Border = 0;
            mainTable.AddCell(cellMiddle);

            PdfPTable entityTable = new PdfPTable(1);
            entityTable.HorizontalAlignment = PdfPCell.ALIGN_RIGHT;
            PdfPCell cellLegalEntity = new PdfPCell(new Phrase(selector1.Process(dicStatement["Legal Entity"])));
            cellLegalEntity.Border = 0;
            cellLegalEntity.HorizontalAlignment = PdfPCell.ALIGN_RIGHT;
            entityTable.AddCell(cellLegalEntity);

            PdfPCell cellStreetAddress = new PdfPCell(new Phrase(selector1.Process(dicStatement["Street Address"])));
            cellStreetAddress.Border = 0;
            cellStreetAddress.HorizontalAlignment = PdfPCell.ALIGN_RIGHT;
            entityTable.AddCell(cellStreetAddress);

            PdfPCell cellCityAddress = new PdfPCell(new Phrase(selector1.Process(dicStatement["City Address"])));
            cellCityAddress.Border = 0;
            cellCityAddress.HorizontalAlignment = PdfPCell.ALIGN_RIGHT;
            entityTable.AddCell(cellCityAddress);

            PdfPCell cellTelephone = new PdfPCell(new Phrase(selector1.Process(dicStatement["Telephone"])));
            cellTelephone.Border = 0;
            cellTelephone.HorizontalAlignment = PdfPCell.ALIGN_RIGHT;
            entityTable.AddCell(cellTelephone);

            PdfPCell cellCVR = new PdfPCell(new Phrase(selector1.Process(dicStatement["CVR"])));
            cellCVR.Border = 0;
            cellCVR.HorizontalAlignment = PdfPCell.ALIGN_RIGHT;
            entityTable.AddCell(cellCVR);


            PdfPCell cellRight = new PdfPCell();
            cellRight.Border = 0;
            cellRight.AddElement(entityTable);
            mainTable.AddCell(cellRight);
            mainTable.WriteSelectedRows(0, -1, document.Left, document.Top, writer.DirectContent);

            //logoImage = null;
        }

        // write on start of each page
        public override void OnStartPage(PdfWriter writer, Document document)
        {
            base.OnStartPage(writer, document);
        }

        // write on end of each page (For Footer)
        public override void OnEndPage(PdfWriter writer, Document document)
        {
            Font standardFooterFont = new Font(Font.FontFamily.HELVETICA, 8, Font.NORMAL, BaseColor.BLACK);
            Font smallFooterFont = new Font(Font.FontFamily.HELVETICA, 6, Font.NORMAL, BaseColor.BLACK);

            base.OnEndPage(writer, document);
            iPageNumber += 1;

            var dateString = DateTime.Parse(dicStatement["Version Date"]);
            string VersionDate = dateString.ToString("dd.MMM.yyyy");

            var isDateValid = dicStatement["Last Reviewed"];
            string ReviewewdDate = "";
            if (String.IsNullOrEmpty(isDateValid))
            {
                ReviewewdDate = "";
            }
            else
            {
                var dateString1 = DateTime.Parse(isDateValid);
                ReviewewdDate = dateString1.ToString("dd.MMM.yyyy");
            }


            float[] tblColWidth = new float[] { 105, 80, 125, 140, 45 };

            PdfPTable tblFooter = new PdfPTable(5);
            tblFooter.SpacingAfter = 1F;
            tblFooter.SetTotalWidth(tblColWidth);

            PdfPCell cell1Footer = new PdfPCell(new Phrase("ID: " + dicStatement["Tracking#"], standardFooterFont));
            cell1Footer.BorderWidthLeft = 0;
            cell1Footer.BorderWidthRight = 0;
            cell1Footer.BorderWidthTop = 1f;
            cell1Footer.BorderWidthBottom = 0;
            tblFooter.AddCell(cell1Footer);

            PdfPCell cell2Footer = new PdfPCell(new Phrase("Version#: " + dicStatement["Version#"], standardFooterFont));
            cell2Footer.BorderWidthLeft = 0;
            cell2Footer.BorderWidthRight = 0;
            cell2Footer.BorderWidthTop = 1f;
            cell2Footer.BorderWidthBottom = 0;
            tblFooter.AddCell(cell2Footer);

            PdfPCell cell3Footer = new PdfPCell(new Phrase("Issue Date: " + VersionDate, standardFooterFont));
            cell3Footer.BorderWidthLeft = 0;
            cell3Footer.BorderWidthRight = 0;
            cell3Footer.BorderWidthTop = 1f;
            cell3Footer.BorderWidthBottom = 0;
            tblFooter.AddCell(cell3Footer);

            PdfPCell cell4Footer = new PdfPCell(new Phrase("Reviewed Date: " + ReviewewdDate, standardFooterFont));
            cell4Footer.BorderWidthLeft = 0;
            cell4Footer.BorderWidthRight = 0;
            cell4Footer.BorderWidthTop = 1f;
            cell4Footer.BorderWidthBottom = 0;
            tblFooter.AddCell(cell4Footer);

            PdfPCell cell5Footer = new PdfPCell(new Phrase("Page No: " + iPageNumber, smallFooterFont));
            cell5Footer.HorizontalAlignment = Element.ALIGN_RIGHT;
            cell5Footer.BorderWidthLeft = 0;
            cell5Footer.BorderWidthRight = 0;
            cell5Footer.BorderWidthTop = 1f;
            cell5Footer.BorderWidthBottom = 0;
            tblFooter.AddCell(cell5Footer);

            tblFooter.WriteSelectedRows(0, -1, document.Left, document.Bottom, writer.DirectContent);

            var getStatus = Convert.ToString(dicStatement["Status"]);
            if (getStatus != "Final")
            {
                // document.Add(draftLogo);
                writer.DirectContent.AddImage(draftLogo);
            }
        }

        //write on close of document
        public override void OnCloseDocument(PdfWriter writer, Document document)
        {
            base.OnCloseDocument(writer, document);
        }
    }

    [DataContract(Name = "CustomImageTagProcessor", Namespace = "http://functions")]
    public class CustomImageTagProcessor : iTextSharp.tool.xml.html.Image
    {
        public override IList<IElement> End(IWorkerContext ctx, Tag tag, IList<IElement> currentContent)
        {
            IDictionary<string, string> attributes = tag.Attributes;
            string src;
            if (!attributes.TryGetValue(HTML.Attribute.SRC, out src))
                return new List<IElement>(1);

            if (string.IsNullOrEmpty(src))
                return new List<IElement>(1);

            if (src.StartsWith("data:image/", StringComparison.InvariantCultureIgnoreCase))
            {
                // data:[<MIME-type>][;charset=<encoding>][;base64],<data>
                var base64Data = src.Substring(src.IndexOf(",") + 1);
                var imagedata = Convert.FromBase64String(base64Data);
                var image = iTextSharp.text.Image.GetInstance(imagedata);

                var list = new List<IElement>();
                var htmlPipelineContext = GetHtmlPipelineContext(ctx);
                list.Add(GetCssAppliers().Apply(new Chunk((iTextSharp.text.Image)GetCssAppliers().Apply(image, tag, htmlPipelineContext), 0, 0, true), tag, htmlPipelineContext));
                return list;
            }
            else
            {
                return base.End(ctx, tag, currentContent);
            }
        }
        protected IList<IElement> CreateElementList(IWorkerContext ctx, Tag tag, iTextSharp.text.Image image)
        {
            var htmlPipelineContext = GetHtmlPipelineContext(ctx);
            var result = new List<IElement>();
            var element = GetCssAppliers().Apply(new Chunk((iTextSharp.text.Image)GetCssAppliers().Apply(image, tag, htmlPipelineContext), 0, 0, true), tag, htmlPipelineContext);
            result.Add(element);

            return result;
        }
    }


}//namespace closed
