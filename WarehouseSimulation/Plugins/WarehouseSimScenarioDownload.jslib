mergeInto(LibraryManager.library, {
  WarehouseSimDownloadText: function (fileNamePtr, mimeTypePtr, contentPtr) {
    var fileName = UTF8ToString(fileNamePtr);
    var mimeType = UTF8ToString(mimeTypePtr);
    var content = UTF8ToString(contentPtr);
    try {
      var blob = new Blob([content], { type: mimeType || "application/json" });
      var url = URL.createObjectURL(blob);
      var a = document.createElement("a");
      a.href = url;
      a.download = fileName || "scenario.json";
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
    } catch (e) {
      console.error("WarehouseSimDownloadText failed", e);
    }
  }
});