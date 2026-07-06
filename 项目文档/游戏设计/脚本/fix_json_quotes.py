import json, os

fp = os.path.join("a:", os.sep + "通用工作区模板", "工作流", "GDD分析.json")
with open(fp, "r", encoding="utf-8") as f:
    txt = f.read()

# Find all positions of unescaped double quotes inside JSON string values
# Strategy: track JSON string state, replace internal quotes with \u201c
result = []
in_str = False
i = 0
while i < len(txt):
    c = txt[i]
    if c == '"' and not in_str:
        in_str = True
        result.append(c)
    elif c == '"' and in_str:
        # Determine if this ends the JSON string value
        j = i + 1
        while j < len(txt) and txt[j] in " \n\r\t":
            j += 1
        if j < len(txt) and txt[j] in ",]}\n:":
            in_str = False
            result.append(c)
        else:
            # This is a Chinese quote inside the string value
            result.append("\\u201c")
    else:
        result.append(c)
    i += 1

new_txt = "".join(result)
with open(fp, "w", encoding="utf-8") as f:
    f.write(new_txt)

try:
    data = json.loads(new_txt)
    print("JSON OK! " + str(len(data["steps"])) + " steps")
except json.JSONDecodeError as e:
    print("Err line " + str(e.lineno) + " col " + str(e.colno))
    ctx = new_txt[max(0, e.pos - 40) : e.pos + 40]
    print(repr(ctx))
