<%@ Page Language="C#" MasterPageFile="~/Views/Shared/SiteTemplate.Master" Inherits="System.Web.Mvc.ViewPage<QuizViewData>" %>
<asp:Content ID="Content1" ContentPlaceHolderID="MainContentPlaceHolder" runat="server">

    <h2>Konkurs</h2>
    <% if (DateTime.Compare(Model.StartDate,DateTime.Now) == 1)
 {%>
 <p>Ju� jutro pojawi si� nowy konkurs.</p>
    <%
 }%>
    <% else if (DateTime.Compare(Model.StartDate,DateTime.Now) < 0)
 {%>
    <p>Mamy przyjemno�� og�osi� konkurs na stronach <a href="http://dotnetomaniak.pl">http://dotnetomaniak.pl</a>.</p>
    <p>Wszystko to mo�liwe dzi�ki naszym sponsorom:</p>
    <ul style="list-style: none">
        <li><a href="http://telerik.com"><img src="<%=Url.Image("Telerik_logo.png")%>" alt="Telerik" /></a></li>
        <li><a href="http://microsoft.com/poland/"><img src="<%= Url.Image("Microsoft.png") %>" alt="Microsoft Polska" /></a></li>
        <li><a href="http://red-gate.com"><img src="<%= Url.Image("redgate.jpg") %>" alt="Red-Gate" /></a></li>
        <li><a href="http://jetbrains.com"><img src="<%= Url.Image("logo_jetbrains.gif") %>" alt="jetbrains"/></a></li>
        <li><a href="http://polishwords.com.pl"><img src="<%=Url.Image("Polishwords300x73.png")%>" alt="Strona Polishwords" /></a></li>        
    </ul>
    <p>Strona <a href="http://polishwords.com.pl">http://polishwords.com.pl</a> zosta�a tak�e patronem medialnym konkursu.</p>
    <p>Nagrody mamy naprawd� imponuj�ce - na prawd� jest o co walczy�:</p> 
    <ul>
    <li>Telerik Premium Collection</li>
    <li>Red-gate's ANTS Memory Profiler</li>
    <li>JetBrains's dotTrace</li>
    <li>kamery internetowe oraz myszki</li>
    <li>koszulki, ksi��ki, gad�ety</li>
    </ul>        
    <p>A teraz ju� bez zb�dnego tracenia czasu zach�cam do zapoznania si� z regulaminem i udzia�u w konkursie. A potem do pisania i wyszukiwania tekst�w o .NET. Powodzenia.</p>
    <p><a href="<%=Url.File("regulamin.pdf")%>" target="_blank">Regulamin konkursu</a></p>    
    <h3>Sponsorzy</h3>    
    <p>Podzi�kujcie naszym sponsorom odwiedzaj�c ich strony. By� mo�e w przysz�o�ci obdaruj� nas kolejnymi nagrodami.</p>
    <ul>
        <li><p>Telerik - Telerik is a leading vendor of User Interface (UI) components for Microsoft .NET technologies � ASP.NET AJAX, Silverlight, WinForms and WPF, and .NET Reporting and content management solutions. Building on its expertise in interface development and Microsoft technologies, Telerik helps customers build applications with unparalleled richness, responsiveness and interactivity. Created with passion, Telerik products help thousands of developers every day to be more productive and deliver reliable applications under budget and on time.</p></li>
        <li><p>Microsoft - Microsoft jest �wiatowym liderem w produkcji oprogramowania, �wiadczeniu us�ug i technologiach internetowych, przeznaczonych do u�ytku osobistego i biznesowego. Za�o�ona w 1975 firma oferuje szeroki zakres produkt�w i us�ug, zaprojektowanych aby pomaga� ludziom i organizacjom w realizowaniu potencja�u.<br />Wi�cej informacji na stronie: <a href="http://www.microsoft.com">http://www.microsoft.com</a></p></li>
        <li><p>RedGate - Red Gate Software makes ingeniously simple tools for over 500,000 Microsoft technology professionals working with SQL Server, .NET, and Exchange Server.  The company�s product line includes must-have tools for .NET developers who program in C# or VB.NET. <a href="http://snurl.com/antsmp">Download your free trial of ANTS Memory Profiler</a>, for identifying causes of memory leaks and optimizing the memory usage of your applications.</p></li>
        <li><p>JetBrains - Resharper - The must-have productivity tool for .NET development in Visual Studio, dotTrace Profiler - An intuitive and super fast memory & performance profiler for .NET</p></li>
        <li><p>Polishwords - Polishwords to strona z filmami instrukta�owymi w j�zyku polskim, z kt�rych nauczysz si� pisa� profesjonalne dokumenty, programowa� i nie tylko!</p></li>
    </ul>
    <%
 } else
 {%>
 <p>Aktualnie brak jest konkurs�w.</p>
 <%
 }%>
</asp:Content>
