let websocket;
let cecilifiedCode;
let settings;
let csharpCode;
let focusedEditor; // either csharpCode or cecilifiedCode
let blockMappings = null;
let csharpCodeEditorWidthMultiplier = 0.3;
let toastsToCleanUpBeforeCecilifying = []; // toast with diagnostics (reset before cecilifying) 
let onCecilifySuccessCallbacks = [];

const CecilifierResponseStatus = Object.freeze({
    Success: 0,
    SyntaxError: 1,
    InternalError: 2,
    MissingAssemblies: 3,
    ConnectionEstablished: 4,
});

class CecilifierRequest
{
    constructor(code, options, settings, targetApi, assemblyReferences) {
        this.code = code;        
        this.options = options;
        this.settings = settings;
        this.targetApi = targetApi;
        this.assemblyReferences = assemblyReferences;
    }
}

class WebOptions
{
    constructor(deployKind) {
        this.deployKind = deployKind;
    }
}

function increaseFocusedEditorFontSize() {
    const options = focusedEditor.getRawOptions();

    const newFontSize = options.fontSize +  1;
    focusedEditor.updateOptions({ fontSize: newFontSize });    
}
function decreaseFocusedEditorFontSize() {
    const options = focusedEditor.getRawOptions();

    const newFontSize = options.fontSize -  1;
    focusedEditor.updateOptions({ fontSize: newFontSize });
}
function initializeSite(
    errorAccessingGist, // error message if accessing the gist produced an error. 
    gist, // contents of a gist; this is set when user navigates to https://cecilifier.me?gistid=[gistid] 
    requestPath, // something like `/name` where `name` is the string used to store the snippet in the browser local store.
    removeStoredSnippet, // user has requested to remove the contents of a snippet from the browser local store
    pageNotFoundPath, // if user navigated to a non-existing page in cecilifier this will contain the path of the page.
    cecilifierVersion,
    frameWorkVersion,
    buildDate,
    buildGitRevision) {
    
    if (pageNotFoundPath != null && pageNotFoundPath.length > 0) {
        SnackBar({
            message: `Requested path ${pageNotFoundPath} is not valid.`,
            dismissible: true,
            status: "Warning",
            timeout: 30000,
            icon: "exclamation"
        });
    }
        
    require.config({ paths: { vs: 'lib/node_modules/monaco-editor/min/vs' } });

    require(['vs/editor/editor.main'], function () {
        csharpCode = monaco.editor.create(document.getElementById('csharpcode'), {
            theme: "vs-dark",
            value:"",
            language: 'csharp',
            minimap: { enabled: false },
            fontSize: 14,
            glyphMargin: false,
            renderWhitespace: false,
            lineNumbersMinChars: 3,
            wordWrap: "on",
        });
        
        cecilifiedCode = monaco.editor.create(document.getElementById('cecilifiedcode'), {
            theme: "vs-dark",
            value: "",
            language: 'csharp',
            readOnly: true,
            minimap: { enabled: false },
            fontSize: 14,
            glyphMargin: false,
            renderWhitespace: false,
            lineNumbersMinChars: 3,
         });
         
        setupCursorTracking();

        window.onresize = function(ev) {
            updateEditorsSize();
        }        
        updateEditorsSize();

        initializeFormattingSettings();

        configureKeyboardShortcuts();

        setSendToDiscordTooltip();

        initializeHoverProvider()
        
        handleInitialSnippet(gist, errorAccessingGist, requestPath, removeStoredSnippet, 
            `// Supported CSharp language version: ${document.getElementById('supportedCSharpVersion').innerText}\n// https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-${document.getElementById('supportedCSharpVersion').innerText}
using System;
class Foo
{
    void Bar() => Console.WriteLine("Hello World!");
}`);
        
        initializeFromPreviousSession();
        
        csharpCode.focus();
        focusedEditor = csharpCode;
    });

    showListOfFixedIssuesInStagingServer(false);
    
    setTooltips(cecilifierVersion, frameWorkVersion, buildDate, buildGitRevision);
    initializeWebSocket();
    disableScroll();
}

function configureKeyboardShortcuts() {
    csharpCode.addCommand(monaco.KeyMod.CtrlCmd + monaco.KeyMod.Alt + monaco.KeyCode.KeyD, function() {
        simulateClick("downloadProject");
    });

    csharpCode.addCommand(monaco.KeyMod.CtrlCmd + monaco.KeyMod.Alt + monaco.KeyCode.KeyC, function() {
        simulateClick("sendbutton");
    });

    csharpCode.addCommand(monaco.KeyMod.CtrlCmd + monaco.KeyMod.Alt + monaco.KeyCode.KeyS, function() {
        changeCecilifierSettings();
    });

    csharpCode.addCommand(monaco.KeyMod.Alt + monaco.KeyCode.LeftArrow, function() {
        csharpCodeEditorWidthMultiplier -= 0.01;
        updateEditorsSize();
    });

    csharpCode.addCommand(monaco.KeyMod.Alt + monaco.KeyCode.RightArrow, function() {
        csharpCodeEditorWidthMultiplier += 0.01;
        updateEditorsSize();
    });

    csharpCode.addCommand(monaco.KeyMod.CtrlCmd + monaco.KeyCode.BracketLeft , decreaseFocusedEditorFontSize);
    csharpCode.addCommand(monaco.KeyMod.CtrlCmd + monaco.KeyCode.BracketRight , increaseFocusedEditorFontSize);

    csharpCode.addCommand(monaco.KeyMod.CtrlCmd + monaco.KeyCode.Period, function() {
        ShowErrorDialog("Report a new issue", "Please be kind, check the existing ones before filing a new issue..", "Report an error.");
    });

    configureCursorInformationVisibility(settings.isEnabled(NamingOptions.ShowCursorPosition));
    csharpCode.addCommand(monaco.KeyMod.CtrlCmd + monaco.KeyMod.Alt + monaco.KeyCode.KeyP , () => {
        configureCursorInformationVisibility(!settings.isEnabled(NamingOptions.ShowCursorPosition));
    });

    updateCursorInformation(csharpCode);
    csharpCode.onDidChangeCursorSelection((e) => {
        updateCursorInformation(csharpCode);
    });

    csharpCode.addCommand(monaco.KeyMod.CtrlCmd + monaco.KeyMod.Alt + monaco.KeyCode.KeyL, function() {
        showListOfLocallyStoredSnippets(csharpCode);
    });

    // Cecilified code
    cecilifiedCode.addCommand(monaco.KeyMod.CtrlCmd + monaco.KeyCode.BracketLeft , decreaseFocusedEditorFontSize);
    cecilifiedCode.addCommand(monaco.KeyMod.CtrlCmd + monaco.KeyCode.BracketRight , increaseFocusedEditorFontSize);
}

function configureCursorInformationVisibility(show) {
    let info = document.getElementById("doc_information");
    info.style.display = show ? "flex" : "none";
    settings.setEnabled(NamingOptions.ShowCursorPosition, show);
}

function updateCursorInformation(ed) {
    let pos = ed.getPosition();
    let sel = ed.getSelection();
    
    let selectedText = "";
    if (sel.startColumn !== sel.endColumn && sel.startLineNumber === sel.endLineNumber)
        selectedText = ` (${sel.endColumn - sel.startColumn} selected)`;
    
    let info = document.getElementById("doc_information");
    info.innerText = `Ln ${pos.lineNumber}, Col ${pos.column}${selectedText}`;
}

function removeMarkersFromCSharpCode() {
    const markers = []
    monaco.editor.setModelMarkers(csharpCode.getModel(), "Errors", markers);

    while (toastsToCleanUpBeforeCecilifying.length > 0)
    {
        const toast = toastsToCleanUpBeforeCecilifying.pop();
        toast.Close();
    }
}
/*
 * Retrieves all open issues with a label 'fixed-in-staging' and, if there are any issues with a reported date
 * newer than the last reported date stored locally, shows a notification with those issues.
 * 
 * After retrieving store reported data for the issue reported most recently. This date will be used
 * to decided whether user should be presented the issues next time he visits the site. 
 **/
function showListOfFixedIssuesInStagingServer(force) {
    if (force) {
        window.localStorage.setItem("last_shown_issue_number", "0");
    }
        
    retrieveListOfFixedIssuesInStagingServer(function (issues) {
        if (issues.length === 0)
            return;

        let lastShownIssueNumber = Number.parseInt(window.localStorage.getItem("last_shown_issue_number")  ?? "0");
        let sortedIssues =issues.sort( (rhs, lhs) => Date.parse(lhs.updated_at) - Date.parse(rhs.updated_at) );
        if (Date.parse(sortedIssues[0].updated_at) <= lastShownIssueNumber)
            return;

        let issuesHtml = sortedIssues.reduce( (previous, issue) => `${previous}<br /><a style='color:#3c763d' href='${issue.html_url}'>${issue.title}</a>`, "List of resolved issues/improvements in <a style='color:#3c763d' href='http://staging.cecilifier.me'>staging server</a><br/>");
        if (sortedIssues.length  === 0)
            return;

        window.localStorage.setItem("last_shown_issue_number", Date.parse(sortedIssues[0].updated_at) + "");
        SnackBar({
            message: issuesHtml,
            dismissible: true,
            status: "Info",
            timeout: 120000,
            icon: "exclamation"
        });
    });
}

function retrieveListOfFixedIssuesInStagingServer(callback) {
    const xhttp = new XMLHttpRequest();
    xhttp.onreadystatechange = function() {
        if (this.readyState === 4 && this.status === 200) {
            try {
                let issues = JSON.parse(this.responseText);
                callback(issues);
            }
            catch (e) {
                SnackBar({
                    message: `Unable to retrieve list of fixed issues in staging server.`,
                    dismissible: true,
                    status: "Warning",
                    timeout: 30000,
                    icon: "exclamation"
                });
                
                console.log(`Unable to retrieve list of fixed issues in staging server: ${e}\nJSON:\n${this.responseText}`);
            }
        }
        else if (this.readyState === 4 && this.status === 500) {
            SnackBar({
                message: this.responseText,
                dismissible: true,
                status: "Error",
                timeout: 120000,
                icon: "exclamation"
            });
        }
    };
    xhttp.open("GET", `${window.origin}/issues_closed_in_staging`, true);
    xhttp.send();
}

function initializeFormattingSettings() {
    const formatSettingsExampleCode = `/*[Obsolete] class AClass<T>
{
    public int field;
    public event Action AnEvent;
    public string Property { get { return "P"; } }
    public void Method() {}
}

struct AStruct { }
enum AnEnum { }
interface Interface {}
record TheRecord();
delegate void ADelegate(int i);
*/
//ClassDeclaration : AClass
var cls_AClass_0 = new TypeDefinition("", "AClass\`1", TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.NotPublic, assembly.MainModule.TypeSystem.Object);

var gp_T_1 = new Mono.Cecil.GenericParameter("T", cls_AClass_0) ;

var attr_Obsolete_2 = new CustomAttribute(assembly.MainModule.ImportReference(typeof(System.ObsoleteAttribute).GetConstructor(new Type[0] {  })));

var fld_field_3 = new FieldDefinition("field", FieldAttributes.Public, assembly.MainModule.TypeSystem.Int32);

var git_AClass_8 = cls_0.MakeGenericInstanceType(cls_AClass_0.GenericParameters.ToArray());

var evt_AnEvent_13 = new EventDefinition("AnEvent", EventAttributes.None, assembly.MainModule.ImportReference(typeof(Action)));
    
var prop_Property_14 = new PropertyDefinition("Property", PropertyAttributes.None, assembly.MainModule.TypeSystem.String);

var md_Method_19 = new MethodDefinition("Method", MethodAttributes.Public | MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Void);

var p_value_21 = new ParameterDefinition("value", ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32);

var il_addAnEvent_7 = md_Method_19.Body.GetILProcessor();

var ctor_AClass_22 = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.RTSpecialName | MethodAttributes.SpecialName, assembly.MainModule.TypeSystem.Void);

var call_AClass_1 = il_addAnEvent_7.NewInstruction(OpCodes.Call, ctor_AClass_22);

var cctor_AClass_42 = new MethodDefinition(".cctor", MethodAttributes.Static | MethodAttributes.Private| MethodAttributes.RTSpecialName | MethodAttributes.SpecialName | MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Void);

var st_AStruct_27 = new TypeDefinition("", "AStruct", TypeAttributes.SequentialLayout | TypeAttributes.Sealed |TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.NotPublic, assembly.MainModule.TypeSystem.Object);

var e_AnEnum_28 = new TypeDefinition("", "AnEnum", TypeAttributes.Private | TypeAttributes.Sealed, assembly.MainModule.ImportReference(typeof(System.Enum)));

var itf_Interface_27 = new TypeDefinition("", "Interface", TypeAttributes.Interface | TypeAttributes.Abstract | TypeAttributes.NotPublic);

var del_ADelegate_30 = new TypeDefinition("", "ADelegate", TypeAttributes.Sealed | TypeAttributes.Private, assembly.MainModule.ImportReference(typeof(System.MulticastDelegate))) { IsAnsiClass = true };

var rec_ARecord_0 = new TypeDefinition("", "ARecord", TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.NotPublic, assembly.MainModule.TypeSystem.Object);

var lv_i_4 = new VariableDefinition(assembly.MainModule.TypeSystem.Int32);

var mr_Foo_5 = new MethodReference("Foo");

var lbl_jump_3 = il_get_11.Create(OpCodes.Nop);`;
    
    let formattingSettingsSample = monaco.editor.create(document.getElementById('_formattingSettingsSample'), {
        theme: "vs-dark",
        value: formatSettingsExampleCode,
        renderWhitespace: false,
        language: 'csharp',
        readOnly: false,
        minimap: { enabled: false },
        fontSize: 16,
        glyphMargin: true,
        scrollbar : {
            vertical: "hidden"
        }        
    });

    let sampleDiv = document.getElementById("_formattingSettingsSample");
    let h =  window.innerHeight;
    let w =  window.innerWidth * 0.75;
    sampleDiv.style.height = `${h}px`;
    sampleDiv.style.width = `${w}px`;

    formattingSettingsSample.layout();

    initializeSettings(formattingSettingsSample);
}

function disableScroll() {
    // Get the current page scroll position
    const scrollTop = window.scrollY || document.documentElement.scrollTop;
    const scrollLeft = window.scrollX || document.documentElement.scrollLeft;
  
    // if any scroll is attempted, set this to the previous value
    window.onscroll = function() {
        window.scrollTo(scrollLeft, scrollTop);
    };
}

function updateEditorsSize() {
    let csharpCodeDiv = document.getElementById("csharpcode");
    let h = window.innerHeight * 0.94;
    let w = window.innerWidth * csharpCodeEditorWidthMultiplier;
    csharpCodeDiv.style.height = `${h}px`;
    csharpCodeDiv.style.width = `${w - 1}px`;

    const cecilifiedCodeDiv = document.getElementById("cecilifiedcode");
    cecilifiedCodeDiv.style.height = `${h}px`;
    cecilifiedCodeDiv.style.width = `${window.innerWidth - w}px`;

    csharpCode.layout();
    cecilifiedCode.layout();
}

function setTooltips(cecilifierVersion,
                     frameWorkVersion,
                     buildDate,
                     buildGitRevision) {
    let msg = `Cecilifier is meant to make it easier to learn how to use <a href="https://github.com/jbevain/cecil" target="_blank">Mono.Cecil</a>.<br/>You can read more details about it in its <a href="https://programing-fun.blogspot.com/2019/02/making-it-easier-to-getting-started.html" target="_blank">blog announcement</a>.<br/><br/>Version ${cecilifierVersion} (${frameWorkVersion})<br/>Git Commig: <a href="https://github.com/adrianoc/cecilifier/commit/${buildGitRevision}">${buildGitRevision}</a></br>Built on: ${buildDate}`;
    
    let defaultDelay =  [500, null];
    tippy('#aboutSpan2', {
        content: msg,
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay,
        maxWidth: 'none'
    });

    tippy('#csharpcode-container', {
        content: "Type any valid C# code to generate the equivalent Cecil API calls.",
        placement: 'bottom',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#cecilifiedcode-container', {
        content: "After pressing <i>Cecilify your code!</i> button, the generate code will be show here.",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#sendbutton', {
        content: "After entering the code you want to process, press this button.<br />(Ctrl-Alt-C)",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#downloadProject', {
        content: "Use this option if you want to download a .Net project (.csproj) ready for you to play with!<br/>(Ctrl-Alt-D)",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#copyToClipboard', {
        content: "Copy cecilified code to clipboard.",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#changeSettings', {
        content: "Change various Cecilifier options.<br/><br/>Use this to configure how variables are named in the cecilified code.<br/>(Ctrl-Alt-S)",
        placement: 'top',
        interactive: true,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#keyboard-shortcuts', {
        content: "<style>\
                    table, td, th { border: none ; }\
                    table {\
                      border-collapse: collapse;\
                      width: 100%;\
                    }\
                    th { text-align: left; }\
                    table.shortcuts td:nth-child(1) { text-align: right; }\
                    table.shortcuts td:nth-child(2) { width: 10px; }\
                    table.shortcuts { width: 1000px; }\
                    </style>\
                    <table class='shortcuts'>\
                    <tr>\
                        <td><kbd class=\"kbc-button\">Ctrl</kbd> + <kbd class=\"kbc-button\">Alt</kbd> + <kbd class=\"kbc-button\">C</kbd></td>\
                        <td/>\
                        <td>Cecilify the code.</td>\
                    </tr>\
                    <tr>\
                        <td><kbd class=\"kbc-button\">Ctrl</kbd> + <kbd class=\"kbc-button\">Alt</kbd> + <kbd class=\"kbc-button\">D</kbd></td>\
                        <td/>\
                        <td>Downloads project with cecilified code.</td>\
                    </tr>\
                    <tr>\
                        <td><kbd class=\"kbc-button\">Ctrl</kbd> + <kbd class=\"kbc-button\">Alt</kbd> + <kbd class=\"kbc-button\">S</kbd></td>\
                        <td/>\
                        <td>Opens settings page.</td>\
                    </tr>\
                    <tr>\
                        <td><kbd class=\"kbc-button\">Alt</kbd> + <kbd class=\"kbc-button\">&larr;</kbd></td>\
                        <td/>\
                        <td>Decreases CSharp editor width / increases Cecilified editor width.</td>\
                    </tr>\
                    <tr>\
                        <td><kbd class=\"kbc-button\">Alt</kbd> + <kbd class=\"kbc-button\">&rarr;</kbd></td>\
                        <td/>\
                        <td>Increases CSharp editor width / decreases Cecilified editor width.</td>\
                    </tr>\
                    <tr>\
                        <td><kbd class=\"kbc-button\">Ctrl</kbd> + <kbd class=\"kbc-button\">]</kbd></td>\
                        <td/>\
                        <td>Increases font size.</td>\
                    </tr>\
                    <tr>\
                        <td><kbd class=\"kbc-button\">Ctrl</kbd> + <kbd class=\"kbc-button\">[</kbd></td>\
                        <td/>\
                        <td>Decreases font size.</td>\
                    </tr>\
                    <tr>\
                        <td><kbd class=\"kbc-button\">Ctrl</kbd> + <kbd class=\"kbc-button\">Alt</kbd> + <kbd class=\"kbc-button\">P</kbd></td>\
                        <td/>\
                        <td>Toggles visibility of cursor information.</td>\
                    </tr>\
                    <tr>\
                        <td><kbd class=\"kbc-button\">Ctrl</kbd> + <kbd class=\"kbc-button\">Alt</kbd> + <kbd class=\"kbc-button\">L</kbd></td>\
                        <td/>\
                        <td>Display a list of locally saved snippets that can be reloaded<br/>by navigating to https://cecilifier.me/SAVEDNAME.</td>\
                    </tr>\
                  </table>",
        placement: 'top',
        interactive: false,
        allowHTML: true,
        theme: 'cecilifier-tooltip',        
        delay: defaultDelay,
        maxWidth: 800
    });

    tippy('#report_internal_error_button', {
        content: "By clicking on 'Report' you'll be redirected to github to authenticate/authorize Cecilifier<br/>" +
            "to file an issue on your behalf.<br/><br/>" +
            "Cecilifier will neither store nor use this authorization for any purpose other than reporting the issue on <a href='https://github.com/adrianoc/cecilifier/issues' style='color:#8cbc13; text-decoration: underline'>Cecilifier Repository</a>.",

        placement: 'top',
        appendTo: document.body,
        interactive: true,
        allowHTML: true,
        theme: 'light',
        maxWidth: "none",
        delay: defaultDelay
    });
    
    tippy('#include-snippet', {
        content: "Selecting this checkbox will include the code to be cecilified in the reported issue.<br/>" +
                 "If it does not contain sensitive information please, select the checkbox.<br/>" +
                 "The source code is extremely useful to investigate the issue.",
        
        appendTo: document.body,
        interactive: true,
        allowHTML: true,
        maxWidth: "none",
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#showFixedBugsInStaging', {
        content: "Shows the list of bugs fixed in Staging but not in production yet.<br/>" +
                 "In other words, bugs fixed in http://staging.cecilifier.me but not in https://cecilifier.me",
        
        placement: 'top',
        interactive: false,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#assembly_references_button', {
        content: "Opens the Assembly References dialog.<br/>" +
            "In this dialog you can specify assemblies that your code snippet will reference types from.",

        placement: 'top',
        interactive: false,
        allowHTML: true,
        theme: 'cecilifier-tooltip',
        delay: defaultDelay
    });

    tippy('#assembly_references_list', {
        content:    "To add assemblies simply drag & drop them over the list.<br />" +
                    "Any valid .NET assembly can be used but you should preferablly use a <a href='https://docs.microsoft.com/en-us/dotnet/standard/assembly/reference-assemblies'>reference assembly</a>.<br />" +
                    "Once added to this list you can reference types from those assemblies in your code snippets.<br /><br />" +
                    "Note that those assemblies will be sent to and stored at cecilifier server during the next request to cecilify some code.<br />",                    
        placement: 'bottom',
        interactive: true,
        allowHTML: true,
        theme: 'assembly_references_tip',
        maxWidth: 'none',
        delay: [1000, null]
    });
}

function initializeSettings(formattingSettingsSample) {

    const startLine = 16;
    
    settings = new SettingsManager(formattingSettingsSample, [
        new Setting(ElementKind.Class, {line: startLine, ch: 5}, "Class", "prefix to be used for classes", "AClass", "cls"),
        new Setting(ElementKind.GenericParameter, {line: startLine + 2, ch: 5}, "Generic Parameter", "generic parameters prefix","T", "gp"),
        new Setting(ElementKind.Attribute, {line: startLine + 4, ch: 5}, "Attribute", "attribute prefix","Obsolete", "attr"),
        new Setting(ElementKind.Field, {line: startLine + 6, ch: 5}, "Field", "field prefix","field", "fld"),
        new Setting(ElementKind.GenericInstance, {line: startLine + 8, ch: 5}, "Generic Instance", "generic instance prefix","AClass", "gi"),
        new Setting(ElementKind.Event, {line: startLine + 10, ch: 5}, "Event", "event declaration prefix","AnEvent", "evt"),
        new Setting(ElementKind.Property, {line: startLine + 12, ch: 5}, "Property", "property prefix","Property", "prop"),
        new Setting(ElementKind.Method, {line: startLine + 14, ch: 5}, "Method", "method prefix","Method", "md"),
        new Setting(ElementKind.Parameter, {line: startLine + 16, ch: 5}, "Parameter", "parameter prefix","value", "p"),
        new Setting(ElementKind.IL, {line: startLine + 18, ch: 5}, "IL", "il variable prefix","addAnEvent", "il"),
        new Setting(ElementKind.Constructor, {line: startLine + 20, ch: 5}, "Constructor", "constructor prefix","AClass", "ctor"),
        new Setting(ElementKind.StaticConstructor, {line: startLine + 24, ch: 5}, "Static Constructor", "static constructor prefix","AClass", "cctor"),
        new Setting(ElementKind.Struct, {line: startLine + 26, ch: 5}, "Struct", "struct prefix","AStruct", "st"),
        new Setting(ElementKind.Enum, {line: startLine + 28, ch: 5}, "Enum", "enum prefix","AnEnum", "e"),
        new Setting(ElementKind.Interface, {line: startLine + 30, ch: 5}, "Interface", "interface prefix","Interface", "itf"),
        new Setting(ElementKind.Delegate, {line: startLine + 32, ch: 5}, "Delegate", "delegate prefix","ADelegate", "del"), 
        new Setting(ElementKind.Record, {line: startLine + 34, ch: 5}, "Record", "prefix to be used for records", "ARecord", "rec"),
        new Setting(ElementKind.LocalVariable, {line: startLine + 36, ch: 5}, "Local Variable", "local variable prefix","i", "lv"),
        new Setting(ElementKind.MemberReference, {line: startLine + 38, ch: 5}, "Member Reference", "Member reference prefix","i", "mr"),
        new Setting(ElementKind.Label, {line: startLine + 40, ch: 5}, "Jump Label", "Jump Label Prefix","jump", "lbl"),
        ],
        document.getElementById("cecilifierSettings"));

    settings.validateOptionalFormat = () => {
        const sel = settings.optionalFormatState();

        if ((sel & 0x7) === 0x4) {
            return "Unique id cannot be the only format.";
        }
    
        if  (sel === 0) {
            return "At least one format need to be included in variable names.";
        }
    
        return null;
    };
    
    settings.addHeader("Name prefixes");
    settings.initialize();
    
    settings.addConditionalFormat(
        NamingOptions.PrefixVariableNamesWithElementKind, 
        "Prefix variable name with element kind",
        0, 
        "Variable names will have the related element kind (class, struct, enum, etc) appended.", 
        (setting) => setting.prefix, 
        (enabled) => settings.setEnabled(enabled));

    const conditionalMemberName = settings.addConditionalFormat(
        NamingOptions.AppendElementNameToVariables,
        "Append member name to variable",
        1,
        "Use this option to append the related element name (the name of the class, struct, property, etc) to variable names.",
        (setting) => setting.memberName,
        (enabled) => {
        });

    conditionalMemberName.addConditionalModifier(
        NamingOptions.CamelCaseElementNames, 
        "Camel case variable names", 
        false, 
        (component, enabled) => enabled ? component[0].toLowerCase() + component.substring(1) : component);
         
    settings.addConditionalFormat(
        NamingOptions.SuffixVariableNamesWithUniqueId, 
        "Suffix variable names with unique Id", 
        2, 
        "Use this option to avoid variable names clashes. If the cecilified code is simple enough you may disable this.", 
        (function(setting) { var id = 1; return function() { id++; return id; } } )(), 
        (enabled) => { });

    settings.addBooleanOption(
        NamingOptions.PrefixInstructionsWithILOpCodeName, 
        "Append IL opcode to instruction variables", 
        "Use this option to make it easier to reason about the cecilified code.", 
        (enabled, sampleEditor) => {
            const lineNumber = 37;
            const wap = sampleEditor.getModel().getWordAtPosition({lineNumber: lineNumber, column: 5});
            const newValue = enabled ? "Call" : "inst";
            const separatorPos = wap.word.indexOf("_");
            sampleEditor.executeEdits("toggle-il", [{forceMoveMarkers : false, range: new monaco.Range(lineNumber, wap.startColumn, lineNumber, wap.startColumn + separatorPos), text:newValue }])
        });
    
    settings.addHeader("Miscellaneous");
    settings.addBooleanOption(
        NamingOptions.AddCommentsToMemberDeclarations, 
        "Add comments before type/member declarations", 
        "Such comments may help in correlating the cecilified to the original source.", 
        (enabled, sampleEditor) => {
            const newValue = enabled ? "//ClassDeclaration : AClass" : "";
            sampleEditor.executeEdits("", [{forceMoveMarkers : false, range: new monaco.Range(startLine - 1, 1, startLine - 1, 64), text:newValue }]);
        });

    settings.addBooleanOption(
        NamingOptions.StoreSettingsLocally, 
        "Store settings locally (cookies)", 
        "Enabling this option so Cecilifier will remember your settings.", 
        (setting, sampleEditor) => { });
    
    settings.addBooleanOption(
        NamingOptions.IncludeSourceInErrorReports, 
        "Include source code when reporting failures to developer discord channel", 
        "Enable this to send the code being cecilified to developer (private) discord channel (if disabled only the error message is sent).\nNote that no matter the state of this option messages are sent anonymously.", 
        (setting, sampleEditor) => { });

    settings.addBooleanOption(
        NamingOptions.ShowCursorPosition,
        "Show cursor position",
        "When enabled shows the cursor position for the snippet editor (left pane).",
        (enabled, sampleEditor) => {
            configureCursorInformationVisibility(enabled);
        });

    const storedSettings = getCookie("cecilifier-settings");
    if (storedSettings.length > 0) {
        settings.loadFromJSON(storedSettings);
    }
}

function setSendToDiscordTooltip()
{
    let msgBody = "the source from the top textbox will be sent to an internal discord channel (accessible only to Cecilifier's author)";
    let msg = null;
    if (getSendToDiscordValue())
    {
        msg = `Publish On: ${msgBody}. Preferable if the contents of the code is not sensitive. This helps Cecilifier developer to better understand usage pattern.`;
    }
    else
    {
        msg = "Publish Off: " + msgBody +  " only in case of errors.";
    }
    
    //TODO: Set the tooltip, i.e, use MSG
}

function setAlert(div_id, msg) {
    const div = document.getElementById(div_id);

    if (msg == null) {
        div.style.zIndex = "0";
        div.style.opacity = "0";
        div.style.position = "absolute";
        div.children[1].innerHTML = "";
    } else {
        div.style.zIndex = "100";
        div.style.opacity = "1";
        div.style.position = "relative";
        div.children[1].innerHTML = msg;
        div.style.display = "block";
    }
}

function initializeWebSocket() {
    const scheme = document.location.protocol === "https:" ? "wss" : "ws";
    const port = document.location.port ? (":" + document.location.port) : "";
    const connectionURL = scheme + "://" + document.location.hostname + port + "/connection/ws";

    let newSocket = new WebSocket(connectionURL);
    newSocket.isRetryingToConnect = websocket === undefined ?  0 : websocket.isRetryingToConnect;
    websocket = newSocket;
    
    console.log(`Is trying to reconnect? ${websocket.isRetryingToConnect}`)

    const sendButton = document.getElementById("sendbutton");
    sendButton.onclick = function() {
        const targetApi = document.getElementById('targetApiButton').innerText.indexOf("Mono.Cecil") === -1
            ? "System.Reflection.Metadata"
            : "Mono.Cecil";
        send(websocket, 'C', targetApi);
    };

    const downloadProjectButton = document.getElementById("downloadProject");
    downloadProjectButton.onclick = function() {
        const targetApi = document.getElementById('targetApiButton').innerText.indexOf("Mono.Cecil") === -1
            ? "System.Reflection.Metadata"
            : "Mono.Cecil";
        send(websocket, 'Z', targetApi);
    };

    websocket.onopen = function (event) {
        console.log(`Cecilifier websocket opened: ${event.type}`);
        websocket.isRetryingToConnect = 0;
    };

    websocket.onclose = function (event) {
        // https://www.rfc-editor.org/rfc/rfc6455.html#section-7.4
        const WebSocketNormalClosure = 1000;
        const WebSocketEndPointShutdown = 1001;
        
        console.log(`Cecilifier websocket closed: ${event.reason} (${event.code})`);
        if (event.code !== WebSocketNormalClosure && event.code !== WebSocketEndPointShutdown) {
            if (websocket.isRetryingToConnect === 0) {
                SnackBar({
                    message: "Cecilifier WebSocket was disconnected; trying to reconnect...",
                    dismissible: true,
                    status: "Error",
                    timeout: 5000,
                    icon: "exclamation"
                });

                // avoid risking ending up in a endless recursive call.
                websocket.isRetryingToConnect = 1;
                initializeWebSocket();
            }
        }
    };

    websocket.onerror = function(event) {
        console.error(`Cecilifier websocket error: ${event.type} (${event})`);
    };
    
    websocket.onmessage = function (event) {
        hideSpinner();
        
        // this is where we get the cecilified code back...
        let response = JSON.parse(event.data);
        if (response.status === CecilifierResponseStatus.Success) {
            updateUsageStatisticsToolTip(response);
            
            if (response.kind === 'Z') {
                setTimeout(function() {
                    const buttonId = createProjectZip(base64ToArrayBuffer(response.data), response.mainTypeName + ".zip", 'application/zip');
                    simulateClick(buttonId);
                });
            }
            else {
                cecilifiedCode.setValue(response.data);
            }

            // save the returned mappings used to map between code snippet <-> Cecilified Code.
            blockMappings = response.mappings;
            for(let i = 0; i < onCecilifySuccessCallbacks.length; i++) {
                onCecilifySuccessCallbacks[i].callback(onCecilifySuccessCallbacks[i].state);
            }
            showDiagnostics(response.diagnostics);
            
        } else if (response.status === CecilifierResponseStatus.ConnectionEstablished) {
            let response = JSON.parse(event.data);
            updateUsageStatisticsToolTip(response);
        } else if (response.status === CecilifierResponseStatus.SyntaxError) {
            let compilerErrors = response.errors;
            for(let i = 0; i < compilerErrors.length; i++) {
                compilerErrors[i].severity = monaco.MarkerSeverity.Error;
                
                compilerErrors[i].startLineNumber = compilerErrors[i].lineInformation.startLineNumber;
                compilerErrors[i].endLineNumber = compilerErrors[i].lineInformation.endLineNumber;
                compilerErrors[i].startColumn = compilerErrors[i].lineInformation.startColumn;
                compilerErrors[i].endColumn = compilerErrors[i].lineInformation.endColumn;
            }

            monaco.editor.setModelMarkers(csharpCode.getModel(), "Errors", compilerErrors);
            
            const toast = SnackBar({
                message: `Code contains ${compilerErrors.length} compilation errors.`,
                dismissible: false,
                status: "Error",
                timeout: 120000,
                actions: [
                    {
                        text: "Close this to remove the error markers",
                        dismiss: false,
                        function: removeMarkersFromCSharpCode
                    }
                ]
            });
            toastsToCleanUpBeforeCecilifying.push(toast);
        } else if (response.status === CecilifierResponseStatus.InternalError) {
            ShowErrorDialog("", response.error, "We've got an internal error.");
        } else if (response.status === CecilifierResponseStatus.MissingAssemblies) {
            console.log(`Cecilifier server asked for missing assemblies => ${response.missingAssemblies}`);
            sendMissingAssemblyReferences(response.missingAssemblies, function () {
                send(websocket, response.originalFormat);
            });
        }
    };
}

function showDiagnostics(diagnostics) {
    for (let i = 0; i < diagnostics.length; i++) {
        const diagnosticToast = SnackBar({
            message: diagnostics[i].Message.replace(/(\r)?\n/g, "<br />"),
            dismissible: true,
            status: diagnostics[i].Kind === 3 ? "Error" : "Warning", // map DiagnosticKind.Information and Warning as warning
            timeout: 60000
        });
        
        toastsToCleanUpBeforeCecilifying.push(diagnosticToast);
    }
}

function updateUsageStatisticsToolTip(response) {
    if (document.getElementById("cecilifier-stats")._tippy === undefined) {
        tippy('#cecilifier-stats', {
            content: "N/A",
            placement: 'top',
            interactive: true,
            allowHTML: true,
            theme: 'cecilifier-tooltip',
            delay: [500, null]
        });
    }
    
    document.getElementById("cecilifier-stats")._tippy.setContent(`
        Total: ${response.counter}<br/>
        Clients: ${response.clientsCounter}<br/>
        Maximum: ${response.maximumUnique}<br/>
    `);
}

function getSendToDiscordValue() { return settings.isEnabled(NamingOptions.IncludeSourceInErrorReports); }

function send(websocket, format, targetApi) {
    if (!websocket || websocket.readyState !== WebSocket.OPEN) {
        SnackBar({
            message: "Lost WebSocket connection with Cecilifier site; please, refresh the page.",
            dismissible: true,
            status: "Error",
            timeout: 8000,
            icon: "exclamation"
        });
        
        return;
    }

    setLastUsedTargetApi(targetApi);
    
    removeMarkersFromCSharpCode();

    showSpinner();

    getAssemblyReferencesMetadata(assemblyReferences =>
    {   
        const request = new CecilifierRequest(
                csharpCode.getValue(),
                new WebOptions(format),
                settings.toTransportObject(),
                targetApi,
                assemblyReferences);

        websocket.send(JSON.stringify(request));}
    );
}

function createProjectZip(text, name, type) {
    const buttonId = "dlbtn";
    const dlbtn = document.getElementById(buttonId);
    const file = new Blob([text], {type: type});
    dlbtn.href = URL.createObjectURL(file);
    dlbtn.download = name;
    
    return buttonId;
}

function base64ToArrayBuffer(base64) {
    const binary_string = window.atob(base64);
    const len = binary_string.length;
    const bytes = new Uint8Array(len);
    for (let i = 0; i < len; i++) {
        bytes[i] = binary_string.charCodeAt(i);
    }
    return bytes.buffer;
}

function simulateClick(elementId) {
    const event = new MouseEvent('click', {
        view: window,
        bubbles: true,
        cancelable: true
    });
    const cb = document.getElementById(elementId);
    const cancelled = !cb.dispatchEvent(event);
}

function copyToClipboard(elementId) {
    navigator.clipboard.writeText(cecilifiedCode.getValue("\r\n"));
}

function showSpinner() {
    let spinnerDiv = document.getElementById("spinnerDiv");
    spinnerDiv.style.visibility = "inherit";
}

function hideSpinner() {
    let spinnerDiv = document.getElementById("spinnerDiv");
    spinnerDiv.style.visibility = "hidden";
}

function setupCursorTracking() {
    cecilifiedCode.onMouseDown(function(e) {
        if (focusedEditor !== cecilifiedCode) {
            focusedEditor = cecilifiedCode;
        }

        if (blockMappings === null)
            return;
        
        for(const blockMapping of blockMappings) {
            if (e.target.position === null || e.target.position.lineNumber < blockMapping.Cecilified.Begin.Line || e.target.position.lineNumber > blockMapping.Cecilified.End.Line)
                continue;

            csharpCode.setSelection({
                startColumn: blockMapping.Source.Begin.Column,
                endColumn: blockMapping.Source.End.Column,
                startLineNumber: blockMapping.Source.Begin.Line,
                endLineNumber: blockMapping.Source.End.Line
            });
            csharpCode.revealLineNearTop(blockMapping.Source.Begin.Line);
            return;
        }
    });

    csharpCode.onMouseDown(function(e) {
        if (focusedEditor !== csharpCode) {
            focusedEditor = csharpCode;
        }
        
        if (blockMappings === null)
            return;
        
        let matchIndex = -1;
        for(let i = 0; i < blockMappings.length && matchIndex === -1; i++)
        {
            if (blockMappings[i].Source.Begin.Line === blockMappings[i].Source.End.Line && blockMappings[i].Source.Begin.Line === e.target.position.lineNumber)
            {
                // Single line block... selected position must be in the bounds
                if (e.target.position.column < (blockMappings[i].Source.Begin.Column) || e.target.position.column > (blockMappings[i].Source.End.Column))
                    continue;

                matchIndex = i;                
            }
            else if (e.target.position.lineNumber >= (blockMappings[i].Source.Begin.Line) && e.target.position.lineNumber <= (blockMappings[i].Source.End.Line))
            {
                matchIndex = i;
            }
        }

        if (matchIndex !== -1)
        {
            cecilifiedCode.setSelection({startColumn: blockMappings[matchIndex].Cecilified.Begin.Column, endColumn: blockMappings[matchIndex].Cecilified.End.Column, startLineNumber: blockMappings[matchIndex].Cecilified.Begin.Line, endLineNumber: blockMappings[matchIndex].Cecilified.End.Line});
            cecilifiedCode.revealLineNearTop(blockMappings[matchIndex].Cecilified.Begin.Line);
        }        
    });    
}

/************************************************
 *         Internal Error Handling              *
 ************************************************/
function ShowErrorDialog(title, body, header) {
    window.onresize = (e) => {
        ResizeErrorDialog();
    };
    
    let titleElement =  getErrorTitleElement();
    titleElement.value = title;
    getErrorBodyElement().value = body;

    document.getElementById("bug_reporter_header_id").innerText = header;
    document.getElementsByClassName("modal")[0].classList.add("opened");
    document.getElementsByClassName("modal")[0].style.display="block";
    
    ResizeErrorDialog();
    titleElement.focus();
}

function CloseErrorDialog() {
    document.getElementsByClassName("modal")[0].classList.remove("opened");
    document.getElementsByClassName("modal")[0].style.display="none";
}

function ResizeErrorDialog(){
    let footerOffset = document.getElementById("bug_reporter_dialog_footer").offsetHeight;
    let h = (document.getElementById("bug_reporter_dialog_id").offsetHeight - footerOffset);

    getErrorTitleElement().style.height = `${h * 0.05}px`;
    getErrorBodyElement().style.height = `${h * 0.70}px`;
}

function updateReportErrorButton(element) {
    UpdateButtonState(getReportErrorButton(), element.value !== "");
}

function UpdateButtonState(element, enable) {
    if (enable) {
        element.classList.remove("btn-disabled");
        element.classList.add("btn");
    } else {
        element.classList.remove("btn");
        element.classList.add("btn-disabled");
    }
}

function fileIssueInGitHub() {
    showSpinner();
    
    try {
        let title = getErrorTitleElement().value;
        if (title === '') {
            getErrorTitleElement().focus();
            return;
        }

        let body = escapeString(getErrorBodyElement().value);
        if (shouldIncludeSnippet()) {
            let code = "```CSharp\\n" + escapeString(csharpCode.getValue()) + "\\n```";
            body = `Error\\n---\\n${body}\\n\\nAssociated snippet:\\n----\\n${code}`;
        }

        window.addEventListener("message", (event) => {
            if (event.origin !== window.origin) {
                console.log(`"Unexpected message from: ${event.origin}`);
                return;
            }
                
            let fileIssueResult = JSON.parse(event.data);
            let snackbarValue = fileIssueResult.status === "success" ?
                {
                    message: `Issue created: ${fileIssueResult.issueUrl}`,
                    dismissible: true,
                    status: "Info",
                    timeout: 6000,
                    icon: "exclamation"
                }
                :
                {
                    message: `Error reporting issue: ${fileIssueResult.message}`,
                    dismissible: true,
                    status: "Error",
                    timeout: 6000,
                    icon: "exclamation"
                };
            CloseErrorDialog();
            SnackBar(snackbarValue);
        }, false);       
        
        window.open(`${window.origin}/fileissue?title=${title}&body=${body}`, '_oauth2', 'width=*,height=*');
    }
    finally {
        hideSpinner();
    }
}

function getErrorTitleElement() {
    return document.getElementById("error_title");
}

function getErrorBodyElement() {
    return document.getElementById("error_description");
}

function getReportErrorButton() {
    return document.getElementById("report_internal_error_button");
}

function shouldIncludeSnippet() {
    return document.getElementById("include-snippet").checked;
}

function AllowDrop(ev) {
    ev.preventDefault();
}

function escapeString(str) {
    return str.replaceAll('"', '\\"')
        .replaceAll('\n', '\\n')
        .replaceAll('\r', '\\r')
        .replaceAll('\f', '\\f')
        .replaceAll('\t', '\\t');
}

// Modal Dialogs
function ResizeDialog(dialogId){
    const dialog = document.getElementById(dialogId);
    let footerOffset = dialog.parentNode.lastChild.offsetHeight;
    let h = (dialog.offsetHeight - footerOffset);

    document.getElementById("assembly_references_header_id").style.height = `${h * 0.02}px`;
}

function ShowModalDialog(id, oncePerDay) {
    if (oncePerDay === true) {
        const cookieName = `modalDialog_${id}_Threshold`;
        const cookie = getCookie(cookieName);
        const today = new Date().toDateString();
        if (cookie !== undefined && new Date(Date.parse(cookie)).toDateString() === today)
            return;
        
        setCookie(cookieName, today, 1);
    }
    const modalDialog = document.getElementById(id);

    modalDialog.parentElement.classList.add("opened");
    modalDialog.parentElement.style.display="block";
}

function CloseModalDialog(id) {
    const assembliesReferenceDialogNode = document.getElementById(id);

    assembliesReferenceDialogNode.parentElement.classList.remove("opened");
    assembliesReferenceDialogNode.parentElement.style.display="none";
}

function initializeFromPreviousSession() {
    const cookieName = `previousSession`;
    const previousSessionStateJson = getCookie(cookieName);
    
    if (previousSessionStateJson.length === 0) {
        return;
    }

    let previousSessionState = JSON.parse(previousSessionStateJson);
    try {
        document.getElementById('targetApiButton').innerText = previousSessionState.targetApi;
    }
    catch (e) {
        console.log(`Error parsing previous session state: ${e}`);
    }
}

function setLastUsedTargetApi(targetApi) {
    const cookieName = `previousSession`;
    const previousSessionState = { targetApi: targetApi };
    setCookie(cookieName, JSON.stringify(previousSessionState), 365);
}