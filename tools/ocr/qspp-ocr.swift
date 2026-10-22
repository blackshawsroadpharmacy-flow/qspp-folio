// qspp-ocr — local Apple Vision OCR worker for the QSPP Manager "Process and Sort" pipeline.
//
// Usage: qspp-ocr <file> [--page N] [--dpi 200]
//   <file>   a PDF (all pages, or just --page N) or a single image (png/jpg/jpeg/tiff/heic/bmp)
//   Output:  JSON on stdout:
//            {"engine":"apple-vision-2 (accurate, macOS 26.x)","osVersion":"…","pages":[{"page":1,"text":"...","confidence":0.93}]}
//
// Everything happens on-device: Vision (VNRecognizeTextRequest) and PDFKit/CoreGraphics only.
// No network access of any kind. Exit codes: 0 ok, 1 usage error, 2 unsupported input,
// 3 recognition produced no text, 10 internal failure.

import AppKit
import PDFKit
import Vision

struct PageResult: Codable {
    let page: Int
    let text: String
    let confidence: Double
}

struct Output: Codable {
    let engine: String
    let osVersion: String
    let pages: [PageResult]
}

func fail(_ code: Int32, _ message: String) -> Never {
    FileHandle.standardError.write((message + "\n").data(using: .utf8)!)
    exit(code)
}

func engineName() -> String {
    let os = ProcessInfo.processInfo.operatingSystemVersion
    return "apple-vision-2 (accurate, macOS \(os.majorVersion).\(os.minorVersion))"
}

/// Raster text recognition with honest rotation handling: try the nominal
/// orientation, then the four 90-degree rotations if the first attempt yields
/// almost nothing. Scanner-fed registers and faxes arrive rotated too often to
/// trust the orientation flag.
func recognize(cgImage: CGImage, pageIndex: Int) -> PageResult? {
    @Sendable func runOnce(_ image: CGImage) throws -> (String, Double) {
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true
        request.recognitionLanguages = ["en-AU", "en-US"]
        let handler = VNImageRequestHandler(cgImage: image, options: [:])
        try handler.perform([request])
        guard let results = request.results, !results.isEmpty else { return ("", 0) }
        var lines: [String] = []
        var confSum = 0.0
        for cand in results {
            guard let top = cand.topCandidates(1).first else { continue }
            lines.append(top.string)
            confSum += Double(top.confidence)
        }
        return (lines.joined(separator: "\n"), lines.isEmpty ? 0 : confSum / Double(lines.count))
    }

    @Sendable func rotated(_ image: CGImage, _ degrees: Int) -> CGImage? {
        guard degrees != 0 else { return image }
        let w = image.width, h = image.height
        let swap = (degrees == 90 || degrees == 270)
        let outW = swap ? h : w
        let outH = swap ? w : h
        guard let ctx = CGContext(data: nil, width: outW, height: outH,
                                  bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        ctx.translateBy(x: CGFloat(outW) / 2, y: CGFloat(outH) / 2)
        ctx.rotate(by: CGFloat(degrees) * .pi / 180)
        let drawW: CGFloat = swap ? CGFloat(h) : CGFloat(w)
        let drawH: CGFloat = swap ? CGFloat(w) : CGFloat(h)
        ctx.draw(image, in: CGRect(x: -drawW / 2, y: -drawH / 2, width: drawW, height: drawH))
        return ctx.makeImage()
    }

    var best: (text: String, conf: Double) = ("", 0)
    for degrees in [0, 90, 180, 270] {
        guard let candidate = rotated(cgImage, degrees) else { continue }
        guard let (text, conf) = try? runOnce(candidate) else { continue }
        if conf > best.conf || (best.text.isEmpty && !text.isEmpty) { best = (text, conf) }
        if best.text.count > 40 && best.conf > 0.5 { break }   // good first-pass exit
    }
    guard !best.text.isEmpty else { return nil }
    return PageResult(page: pageIndex + 1, text: best.text, confidence: (best.conf * 1000).rounded() / 1000)
}

func renderPageViaKit(_ document: PDFDocument, at index: Int, dpi: Int) -> CGImage? {
    guard let page = document.page(at: index) else { return nil }
    let scale = CGFloat(dpi) / 72.0
    let bounds = page.bounds(for: .mediaBox)
    let size = NSSize(width: bounds.width * scale, height: bounds.height * scale)
    // PDFPage.thumbnail renders with PDFKit's own (correctly oriented) machinery;
    // hand-driven NSContext drawing produced mirrored output for image-only pages.
    let image = page.thumbnail(of: size, for: .mediaBox)
    guard image.size.width > 0,
          let tiff = image.tiffRepresentation,
          let rep = NSBitmapImageRep(data: tiff),
          let cg = rep.cgImage else { return nil }
    // thumbnail may come back at fractional device pixel size; snap to the target
    let snapped = NSSize(width: floor(size.width), height: floor(size.height))
    if NSSize(width: cg.width, height: cg.height) == snapped { return cg }
    let target = NSSize(width: snapped.width, height: snapped.height)
    let ctxImg = NSImage(size: target)
    ctxImg.lockFocus()
    NSGraphicsContext.current?.imageInterpolation = .high
    image.draw(in: NSRect(origin: .zero, size: target))
    ctxImg.unlockFocus()
    guard let t2 = ctxImg.tiffRepresentation, let r2 = NSBitmapImageRep(data: t2) else { return nil }
    return r2.cgImage
}

func recognizeImageFile(_ url: URL) -> [PageResult] {
    guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
          let cg = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
        fail(2, "qspp-ocr: cannot decode image \(url.path)")
    }
    guard let page = recognize(cgImage: cg, pageIndex: 0) else {
        fail(3, "qspp-ocr: no text recognised in image \(url.lastPathComponent)")
    }
    return [page]
}

func recognizePdf(_ url: URL, onlyPage: Int?, dpi: Int) -> [PageResult] {
    guard let doc = PDFDocument(url: url) else {
        fail(2, "qspp-ocr: cannot open PDF \(url.path)")
    }
    var results: [PageResult] = []
    let pageCount = doc.pageCount
    let targets: [Int]
    if let only = onlyPage {
        guard only >= 1 && only <= pageCount else {
            fail(1, "qspp-ocr: --page \(only) out of range (1...\(pageCount))")
        }
        targets = [only - 1]
    } else {
        targets = Array(0..<pageCount)
    }
    for index in targets {
        guard let cg = renderPageViaKit(doc, at: index, dpi: dpi) else { continue }
        if let page = recognize(cgImage: cg, pageIndex: index) {
            results.append(page)
        } else {
            results.append(PageResult(page: index + 1, text: "", confidence: 0))
        }
    }
    return results
}

func main() {
    let args = Array(CommandLine.arguments.dropFirst())
    guard let input = args.first, !input.hasPrefix("--") else {
        fail(1, "qspp-ocr: usage: qspp-ocr <file> [--page N] [--dpi 200]")
    }
    let url = URL(fileURLWithPath: input)
    var onlyPage: Int? = nil
    var dpi = 200
    var rest = Array(args.dropFirst())
    while !rest.isEmpty {
        switch rest[0] {
        case "--page":
            guard rest.count >= 2, let n = Int(rest[1]) else { fail(1, "qspp-ocr: --page needs a number") }
            onlyPage = n
            rest = Array(rest.dropFirst(2))
        case "--dpi":
            guard rest.count >= 2, let n = Int(rest[1]), n >= 72, n <= 400 else { fail(1, "qspp-ocr: --dpi needs 72...400") }
            dpi = n
            rest = Array(rest.dropFirst(2))
        default:
            fail(1, "qspp-ocr: unknown argument \(rest[0])")
        }
    }
    let ext = url.pathExtension.lowercased()
    let pages: [PageResult]
    switch ext {
    case "pdf":
        pages = recognizePdf(url, onlyPage: onlyPage, dpi: dpi)
    case "png", "jpg", "jpeg", "tif", "tiff", "heic", "bmp":
        pages = recognizeImageFile(url)
    default:
        fail(2, "qspp-ocr: unsupported input type .\(ext)")
    }
    let out = Output(engine: engineName(),
                     osVersion: ProcessInfo.processInfo.operatingSystemVersionString,
                     pages: pages)
    guard let data = try? JSONEncoder().encode(out) else {
        fail(10, "qspp-ocr: internal error encoding output")
    }
    FileHandle.standardOutput.write(data)
}

main()