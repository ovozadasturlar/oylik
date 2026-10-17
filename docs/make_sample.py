"""
docs/sample.xlsx — sintetik namuna Excel (import testlari, demo seed va snapshot uchun).

Tuzilma mijozning haqiqiy fayli bilan aynan bir xil: «Лист1», A1:I105, ustunlar
№ / Оклад / Ишчилар / Сумма / Кун / Жами (=E*D) / Аванс / Колдик (=F−G), orada bo'sh qatorlar,
qo'lda kunlik (oklad 0), qavsli izohli ismlar, oxirida bo'sh joy qolgan ismlar, Колдик'da formula o'rniga 0.
Ismlar o'ylab topilgan, summalar boshqacha — haqiqiy shaxslarga hech qanday aloqasi yo'q.

Ishga tushirish:  python docs/make_sample.py   (openpyxl kerak: pip install openpyxl)
"""
from pathlib import Path

from openpyxl import Workbook
from openpyxl.formatting.rule import CellIsRule
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.styles.colors import Color

ROWS = 104  # № 1..104 → Excel qatorlari 2..105

# Excel qatori → ism (oxiridagi bo'sh joy ataylab: import Trim qilishi tekshiriladi)
NAMES = {
    2: "Олим ака (тест)",   # Оклад 9 (mln), Сумма = 9 000 000 / 30 → qo'lda emas
    3: "Бахтиёр ",          # Оклад 9, Сумма 300 000
    4: "Сардор ",           # Оклад 8, Сумма bo'sh → 266 667 (hisoblanadi)
    5: "Фаррух ",
    6: "Олим 2 (сип)",
    7: "Анвар ",            # Колдик — formula emas, 0
    8: "Баходир ака ",
    9: "Алишер ",
    10: "Аваз ",
    11: "Бекзод",
    12: "Ботир",
    13: "Вохид",
    18: "Гайрат ",
    22: "Дилмурод ",
    25: "Даврон",
    26: "Жамшид",
    27: "Жалол ака (кор)",
    28: "Зафар ака ",
    30: "Зиёд",
    31: "Иззат",
    35: "Иброхим ака ",
    36: "Исмоил ака",
    37: "Каримжон 70",
    38: "Камол (омбор)",
    39: "Лазиз ака ",
    40: "Тимур (бух)",      # Оклад yo'q (0), Сумма 250 000 → qo'lda kunlik
    41: "Мурод (сип)",
    42: "Мирзо ака (кор)",
    46: "Нурали ака ",
    47: "Носир ",
    50: "Неъмат",
    51: "Обид ",
    52: "Шодмон",
    54: "Рустам (бух)",
    55: "Рустам (омбор)",
    59: "Равшан ",
    65: "Санжар ",
    69: "Тохир (бригадир)",
    70: "Тохир (кор)",
    71: "Улугбек ",
    76: "Фахриддин ака ",
    80: "Хуршид ",
    81: "Хасан ака (сип)",
    82: "Хусан ",
    83: "Хасан ака 60",
    84: "Чори 120",
    85: "Шамсиддин ",
    90: "Эркин ",
    91: "Ёркин",
    96: "Гулнора опа ",
    97: "Нилуфар опа",
    98: "Сабохат опа ",
    99: "Феруза опа",
    100: "Нигора опа ",
    101: "Мохира опа",
    102: "Гулчехра",
    103: "Ошпаз",
}
SALARY = {2: 9, 3: 9, 4: 8}                          # Оклад — mln so'm (qisqa kiritish)
DAILY = {2: 300_000, 3: 300_000, 40: 250_000, 63: 250_000}  # 63 — ismsiz qatorda summa (o'tkazib yuboriladi)
ZERO_REMAINDER = {7}                                 # Колдик = 0 (formula emas)

HEADERS = ["№", "Оклад", "Ишчилар", "Сумма", "Кун", "Жами", "Аванс", "Колдик", None]
WIDTHS = {"A": 7.7109375, "B": 7.0, "C": 26.42578125, "D": 13.0, "F": 22.0, "G": 20.28515625, "H": 22.7109375}
MONEY = '_-* #,##0.00_-;\\-* #,##0.00_-;_-* "-"??_-;_-@_-'
BOLD_COLUMNS = {"A", "C", "D", "F", "G", "H"}


def theme_fill(theme: int, tint: float) -> PatternFill:
    color = Color(theme=theme, tint=tint)
    return PatternFill(fill_type="solid", fgColor=color, bgColor=Color(indexed=64))


def build(path: Path) -> None:
    assert len(NAMES) == 57, len(NAMES)
    wb = Workbook()
    ws = wb.active
    ws.title = "Лист1"
    wb.create_sheet("Лист2")
    wb.create_sheet("Лист3")

    thin = Side(style="thin")
    border = Border(left=thin, right=thin, top=thin, bottom=thin)
    header_fill = theme_fill(3, 0.3999755851924192)
    total_fill = theme_fill(6, -0.249977111117893)
    advance_fill = theme_fill(7, 0.3999755851924192)

    for col, title in enumerate(HEADERS, 1):
        cell = ws.cell(1, col, title)
        cell.font = Font(name="Calibri", size=11, bold=True)
        cell.alignment = Alignment(horizontal="center")
        cell.border = border
        cell.fill = header_fill

    for row in range(2, ROWS + 2):
        values = {
            "A": row - 1,
            "B": SALARY.get(row),
            "C": NAMES.get(row),
            "D": DAILY.get(row),
            "E": None,
            "F": f"=E{row}*D{row}",
            "G": None,
            "H": 0 if row in ZERO_REMAINDER else f"=F{row}-G{row}",
            "I": 0 if row == 2 else None,
        }
        for letter, value in values.items():
            cell = ws[f"{letter}{row}"]
            cell.value = value
            cell.font = Font(name="Calibri", size=11, bold=letter in BOLD_COLUMNS)
            cell.border = border
            if letter in ("A", "B", "E"):
                cell.alignment = Alignment(horizontal="center")
            elif letter == "H":
                cell.alignment = Alignment(horizontal="right")
            if letter in ("D", "F", "G", "H"):
                cell.number_format = MONEY
            if letter == "A":
                cell.fill = header_fill
            elif letter == "F":
                cell.fill = total_fill
            elif letter == "G":
                cell.fill = advance_fill

    for letter, width in WIDTHS.items():
        ws.column_dimensions[letter].width = width
    ws.sheet_view.zoomScale = 115

    # Колдик: musbat — yashil, manfiy — qizil
    ws.conditional_formatting.add("H1:H1048576", CellIsRule(operator="greaterThan", formula=["0"], font=Font(color="FF00B050")))
    ws.conditional_formatting.add("H1:H1048576", CellIsRule(operator="lessThan", formula=["0"], font=Font(color="FFC00000")))

    wb.active = 0
    wb.save(path)


if __name__ == "__main__":
    target = Path(__file__).with_name("sample.xlsx")
    build(target)
    print(f"{target} — {len(NAMES)} ta xodim")
