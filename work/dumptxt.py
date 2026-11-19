import re, sys, zipfile

p = sys.argv[1]
z = zipfile.ZipFile(p)
xml = z.read('word/document.xml').decode('utf-8')
paras = re.findall(r'<w:p[ >].*?</w:p>', xml, re.S)
out = []
for par in paras:
    txt = ''.join(re.findall(r'<w:t[^>]*>(.*?)</w:t>', par, re.S))
    if txt.strip():
        out.append(txt)
print('\n'.join(out[:int(sys.argv[2]) if len(sys.argv) > 2 else 60]))
