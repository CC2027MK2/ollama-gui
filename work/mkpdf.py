import struct, sys, zlib

# 把若干 JPEG 拼成最简单的 PDF（每页一张图，无文字层），用来测试 OCR/翻译流程
jpgs = sys.argv[1].split(',')
out = sys.argv[2]
objs = []

def add(b):
    objs.append(b)
    return len(objs)

n = len(jpgs)
page_ids = []
for f in jpgs:
    d = open(f, 'rb').read()
    if d[:2] != b'\xff\xd8':
        raise SystemExit('not jpeg: ' + f)
    # 从 JPEG 里读宽高
    i = 2
    w = h = 0
    while i < len(d) - 9:
        if d[i] != 0xFF:
            i += 1
            continue
        m = d[i + 1]
        if m in (0xC0, 0xC1, 0xC2):
            h = struct.unpack('>H', d[i + 5:i + 7])[0]
            w = struct.unpack('>H', d[i + 7:i + 9])[0]
            break
        if m in (0xD8, 0xD9):
            i += 2
            continue
        ln = struct.unpack('>H', d[i + 2:i + 4])[0]
        i += 2 + ln
    img = add(b'<< /Type /XObject /Subtype /Image /Width %d /Height %d /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length %d >>\nstream\n' % (w, h, len(d)) + d + b'\nendstream')
    ct = add(b'<< /Type /XObject /Subtype /Image /Width %d /Height %d /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length %d >>\nstream\n' % (w, h, len(d)) + d + b'\nendstream') if False else None
    pw, ph = 595, 842
    content = ('q %.2f 0 0 %.2f 0 0 cm /Im0 Do Q' % (pw, ph)).encode()
    cs = add(b'<< /Length %d >>\nstream\n' % len(content) + content + b'\nendstream')
    page_ids.append((img, cs, pw, ph))

pages_id = n * 3 + 1  # 占位，稍后修正
kids = []
page_objs = []
for (img, cs, pw, ph) in page_ids:
    pid = add(b'')  # 占位
    page_objs.append((pid, img, cs, pw, ph))

catalog = add(b'')
pages = add(b'')

# 回填页面对象
for (pid, img, cs, pw, ph) in page_objs:
    objs[pid - 1] = ('<< /Type /Page /Parent %d 0 R /MediaBox [0 0 %d %d] /Resources << /XObject << /Im0 %d 0 R >> >> /Contents %d 0 R >>' % (pages, pw, ph, img, cs)).encode()
    kids.append('%d 0 R' % pid)

objs[catalog - 1] = ('<< /Type /Catalog /Pages %d 0 R >>' % pages).encode()
objs[pages - 1] = ('<< /Type /Pages /Count %d /Kids [%s] >>' % (n, ' '.join(kids))).encode()

buf = bytearray(b'%PDF-1.4\n')
offsets = []
for i, o in enumerate(objs):
    offsets.append(len(buf))
    buf += ('%d 0 obj\n' % (i + 1)).encode() + o + b'\nendobj\n'
xref = len(buf)
buf += ('xref\n0 %d\n' % (len(objs) + 1)).encode()
buf += b'0000000000 65535 f \n'
for off in offsets:
    buf += ('%010d 00000 n \n' % off).encode()
buf += ('trailer\n<< /Size %d /Root %d 0 R >>\nstartxref\n%d\n%%%%EOF\n' % (len(objs) + 1, catalog, xref)).encode()
open(out, 'wb').write(buf)
print('pages', n, 'bytes', len(buf))
