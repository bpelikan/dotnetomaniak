﻿<%@ Page Language="C#" MasterPageFile="~/Views/Shared/SiteTemplate.Master" Inherits="System.Web.Mvc.ViewPage<SupportViewData>"%>
<script runat="server">

    protected override void OnInit(EventArgs e)
    {
        base.OnInit(e);

        GenerateScript();

        Page.Header.Title = "{0} - Kontakt".FormatWith(Model.SiteTitle);
    }

    private void GenerateScript()
    {
        jQueryScriptManager scriptManager = jQueryScriptManager.Current;

        scriptManager.RegisterSource(Url.Asset("contact"));

        scriptManager.RegisterOnReady("Contact.init();");
        scriptManager.RegisterOnDispose("Contact.dispose();");
    }

</script>
<asp:Content ID="Content1" ContentPlaceHolderID="HeadPlaceHolder" runat="server"></asp:Content>
<asp:Content ID="Content2" ContentPlaceHolderID="MainContentPlaceHolder" runat="server">
    <%= Html.ArticleHeader("Kontakt", new string[] {"Strona główna", "Kontakt"})%>
    <p>Dla szybszego i prostszego kontaktu z nami wypełnij formularz. Szanujemy twoją prywatność. Twój e-mail nie będzie nikomu udostępniany.</p>
    <form id="frmContact" action="<%= Url.Action("Contact", "Support") %>" method="post">
        <fieldset>
            <div class="add-article-row">
                <label for="txtContactName" class="label">Imię:</label>
                <input id="txtContactName" name="name" type="text" class="largeTextBox"/>
                <span class="error"></span>
            </div>
            <div class="add-article-row">
                <label for="txtContactEmail" class="label">Email:</label>
                <input id="txtContactEmail" name="email" type="text" class="largeTextBox"/>
                <span class="error"></span>
            </div>            
            <div class="add-article-row">
                <label for="txtContactMessage" class="label" id="no-padding">Wiadomość:</label>
                <textarea id="txtContactMessage" name="message" class="largeTextArea" cols="52" rows="10" style="width: 436px"></textarea>
                <span class="error"></span>
            </div>
            <p>
                <span id="contactMessage" class="message"></span>
            </p>
            <div class="add-article-row">                
                <input id="btnContactSubmit" type="submit" class="largeButton" value=""/>
            </div>
        </fieldset>
    </form>
</asp:Content>