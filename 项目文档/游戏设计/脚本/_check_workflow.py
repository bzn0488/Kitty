import json, sys
with open(sys.argv[1], 'r', encoding='utf-8-sig') as f:
    text = f.read()
text = text.replace('\u201c', '"').replace('\u201d', '"')
d = json.loads(text)
print(f'步骤数: {len(d["steps"])}')
for i, s in enumerate(d['steps']):
    print(f'  {i+1}. {s["id"]}: {s.get("name","")}')
