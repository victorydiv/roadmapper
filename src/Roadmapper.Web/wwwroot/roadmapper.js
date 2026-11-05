// Small browser helpers called from Blazor via JS interop.
window.roadmapper = {
    /** Fetches an SVG from the server, rasterizes it at up to 2x, and downloads it as a PNG. */
    async downloadSvgAsPng(url, fileName) {
        const response = await fetch(url);
        if (!response.ok) throw new Error(`Export failed (${response.status}).`);
        const svgUrl = URL.createObjectURL(new Blob([await response.text()], { type: "image/svg+xml" }));
        try {
            const img = new Image();
            await new Promise((resolve, reject) => {
                img.onload = resolve;
                img.onerror = () => reject(new Error("The browser couldn't render the timeline image."));
                img.src = svgUrl;
            });
            // Browsers cap canvas size; shrink the scale for very wide timelines.
            const scale = Math.min(2, 16000 / img.naturalWidth, 16000 / img.naturalHeight);
            const canvas = document.createElement("canvas");
            canvas.width = Math.round(img.naturalWidth * scale);
            canvas.height = Math.round(img.naturalHeight * scale);
            const ctx = canvas.getContext("2d");
            ctx.scale(scale, scale);
            ctx.drawImage(img, 0, 0);
            const png = await new Promise(resolve => canvas.toBlob(resolve, "image/png"));
            const link = document.createElement("a");
            link.href = URL.createObjectURL(png);
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(link.href), 10000);
        } finally {
            URL.revokeObjectURL(svgUrl);
        }
    }
};
