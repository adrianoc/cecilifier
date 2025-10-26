let db;

const dbOpenRequest = window.indexedDB.open("assembly-references", 1);

dbOpenRequest.onupgradeneeded = function(event) {
    db = event.target.result;
    let objectStore = db.createObjectStore("assembly-references", { keyPath: "assemblyHash" });
    objectStore.createIndex("assemblyName", "assemblyName");
    objectStore.createIndex("assemblyHash", "assemblyHash");

    let assemblyReferenceContentStore = db.createObjectStore("assembly-reference-content", { keyPath: "assemblyHash" });
    assemblyReferenceContentStore.createIndex("assemblyName", "assemblyName");
    assemblyReferenceContentStore.createIndex("assemblyHash", "assemblyHash");
    assemblyReferenceContentStore.createIndex("base64Contents", "base64Contents");
}

dbOpenRequest.onsuccess = function onsuccess (event) {
    db = event.target.result;
}

function getAssemblyReferencesContents(missingAssemblyHashes, callback) {
    let objectStore = db.transaction(['assembly-reference-content'], "readonly").objectStore('assembly-reference-content');
    let request = objectStore.getAll();

    request.onsuccess = function (event) {
        const missingAssemblies = event.target.result.filter(ar => missingAssemblyHashes.find(candidate => candidate === ar.assemblyHash));
        callback(missingAssemblies);
    };
}

function getAssemblyReferencesMetadata(callback) {
    let objectStore = db.transaction(['assembly-references'], "readonly").objectStore('assembly-references');
    let request = objectStore.getAll();

    request.onsuccess = function (event) {
        callback(event.target.result);
    };
}
function loadAssemblyReferenceMetadata(callback) {
    let objectStore = db.transaction(['assembly-references'], "readonly").objectStore('assembly-references');
    let cursor = objectStore.openCursor();

    cursor.onsuccess = function(event) {
        let cursor = event.target.result;
        if (!cursor)
            return;

        callback(cursor.value);
        cursor.continue();
    };
}
function storeReferenceAssembliesLocally() {
    const assembly_references = document.getElementById("assembly_references_list");
    for (let i = 0; i < assembly_references.options.length; i++) {
        const toStore = {
            assemblyName : assembly_references.options[i].text,
            assemblyHash: assembly_references.options[i].getAttribute("data-assembly-hash"),
        };

        let transaction = db.transaction(["assembly-references", "assembly-reference-content"], "readwrite");
        let assemblyReferencesStore = transaction.objectStore("assembly-references");
        assemblyReferencesStore.add(toStore);

        let assemblyReferenceContentStore = db.transaction(["assembly-reference-content"], "readwrite").objectStore("assembly-reference-content");
        assemblyReferenceContentStore.add({ assemblyName: toStore.assemblyName, assemblyHash: toStore.assemblyHash, base64Contents: assembly_references.options[i].value});
    }
}

function removeSelectedAssemblyReference() {
    const assemblyReferenceList = document.getElementById("assembly_references_list");
    if (assemblyReferenceList.selectedIndex === -1)
        return;

    let assemblyHash = assemblyReferenceList.options[assemblyReferenceList.selectedIndex].getAttribute("data-assembly-hash");
    let transaction = db.transaction(["assembly-references", "assembly-reference-content"], "readwrite");
    transaction.objectStore("assembly-references").delete(assemblyHash);
    db.transaction(["assembly-reference-content"], "readwrite").objectStore("assembly-reference-content").delete(assemblyHash);

    assemblyReferenceList.remove(assemblyReferenceList.selectedIndex);
}

/************************************************
 *         Assembly References Handling         *
 ************************************************/
function ShowAssemblyReferencesDialog(title, header) {
    window.onresize = (e) => {
        ResizeDialog("assembly_references_dialog_id");
    };

    document.getElementById("assembly_references_list").innerHTML = "";

    const assembliesReferenceDialogNode = document.getElementById("assembly_references_dialog_id");

    assembliesReferenceDialogNode.parentElement.classList.add("opened");
    assembliesReferenceDialogNode.parentElement.style.display="block";

    loadAssemblyReferenceMetadata(function(item) {
        AddAssemblyReferenceToList2(item.assemblyName, item.assemblyHash);
    });

    ResizeDialog("assembly_references_dialog_id");
}

function DropAssemblyReference(ev) {
    ev.preventDefault();
    const files = ev.dataTransfer.files;

    let foundDll = false;
    for(let index = 0; index < files.length; index++)
    {
        if (!foundDll && !files[index].name.endsWith(".dll"))
            continue;

        foundDll = true;

        let reader = new FileReader();
        reader.addEventListener("load", function ()
        {
            // returned data has an extra prefix that needs to be removed
            // https://developer.mozilla.org/en-US/docs/Web/API/FileReader/readAsDataURL
            const prefixIndex = this.result.indexOf("base64,");
            AddAssemblyReferenceToList(files[index].name, this.result.substring(prefixIndex + 7));
        });
        reader.readAsDataURL(files[index]);
    }

    if (!foundDll)
        return false;

    UpdateButtonState(document.getElementById("close_assembly_references_dialog"), true);
}

function AddAssemblyReferenceToList2(assemblyName, assemblyHash) {
    let newAssemblyReference = document.createElement("option");
    newAssemblyReference.text = assemblyName;
    newAssemblyReference.setAttribute("data-assembly-hash", assemblyHash);
    newAssemblyReference.addEventListener("click", ev => {
        console.log(`${ev.target.text} : ${ev.target.getAttribute("data-assembly-hash")}`);
    });

    document.getElementById("assembly_references_list").appendChild(newAssemblyReference);

    UpdateButtonState(document.getElementById("close_assembly_references_dialog"), true);
    return newAssemblyReference;
}

function AddAssemblyReferenceToList(assemblyName, base64Contents) {
    let newAssemblyReference = AddAssemblyReferenceToList2(assemblyName, computeSHA256(base64Contents));
    newAssemblyReference.value = base64Contents;
}

function computeSHA256(base64Contents) {
    return sha256(base64Contents);
}

function StoreReferenceAssembliesLocallyAndClose() {
    storeReferenceAssembliesLocally();
    CloseDialog();
}

function sendMissingAssemblyReferences(missingAssemblyHashes, continuation) {
    getAssemblyReferencesContents(missingAssemblyHashes, function (missingAssemblies) {
        const xhttp = new XMLHttpRequest();
        xhttp.onreadystatechange = function() {
            if (this.readyState !== this.DONE)
                return;
            
            if (this.status === 200) {
                continuation();
            }
            else if (this.status === 500) {
                SnackBar({
                        message: this.responseText,
                        dismissible: true,
                        status: "Error",
                        timeout: 30000,
                        icon: "exclamation"
                    });
            }
            else if (this.status !== 0) {
                SnackBar({
                    message:  `Error sending assemblies to cecilifier server.<br /><br />${this.responseText}`,
                    dismissible: true,
                    status: "Error",
                    timeout: 30000,
                    icon: "exclamation"
                });
            }
        };

        xhttp.open("POST", "/referenced_assemblies", true);
        xhttp.setRequestHeader('Content-Type', 'application/octet-stream');
        xhttp.send(JSON.stringify({ assemblyReferences:  missingAssemblies }));
    });
}