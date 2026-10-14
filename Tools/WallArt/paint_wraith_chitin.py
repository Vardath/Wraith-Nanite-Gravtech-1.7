"""Hand-authored Wraith chitin relief drawn on the full 1x1 wall cap.

Built for RimWorld's 192px UV sample. Closed armoured lamellae stay inside the
organic roof surface, not inset sprite figures. Straight wall runs deliberately
omit the junction's luminous nucleus.
"""
def extra_gradients():
    return (
        '<linearGradient id="plates" x1=".1" y1="0" x2=".85" y2="1">'
        '<stop offset="0" stop-color="#98818a"/><stop offset=".18" stop-color="#6b5465"/>'
        '<stop offset=".51" stop-color="#4a3447"/><stop offset=".79" stop-color="#392a3e"/>'
        '<stop offset="1" stop-color="#241d2d"/></linearGradient>'
        '<linearGradient id="carapace" x1="0" y1="0" x2="1" y2=".95">'
        '<stop offset="0" stop-color="#dfc5ac"/><stop offset=".19" stop-color="#bea6a3"/>'
        '<stop offset=".42" stop-color="#8a717e"/><stop offset=".79" stop-color="#54445e"/>'
        '<stop offset="1" stop-color="#33283e"/></linearGradient>'
        '<radialGradient id="eye" cx="50%" cy="45%" r="60%">'
        '<stop offset="0" stop-color="#aec99b" stop-opacity=".92"/>'
        '<stop offset=".25" stop-color="#6a9775" stop-opacity=".65"/>'
        '<stop offset=".56" stop-color="#477c6b" stop-opacity=".20"/>'
        '<stop offset="1" stop-color="#405363" stop-opacity="0"/></radialGradient>'
        '<linearGradient id="chitinShadow" x1="0" y1="0" x2="1" y2="1">'
        '<stop offset="0" stop-color="#3a2835"/>'
        '<stop offset="1" stop-color="#140f1d"/></linearGradient>'
    )

def overlay_scutes(mask, path):
    bits = []
    # Painterly plates have heavy dark roots and narrower ivory-beige
    # edge highlights. Symmetry is relaxed across the organically shaped cap.
    plates = [
        ('M -24 -17 C 32 6 70 6 90 37 C 101 55 89 72 66 85 '
         'C 42 100 12 102 -19 112 Z','url(#chitinShadow)',0,.76),
        ('M -21 -14 C 32 4 65 10 80 38 C 89 51 79 67 60 78 '
         'C 35 91 13 90 -21 101 Z','url(#plates)',3,.95),
        ('M 202 -18 C 167 6 136 16 125 43 C 113 66 130 83 160 93 C 174 98 195 99 215 115 Z','url(#chitinShadow)',0,.86),
        ('M 199 -10 C 167 12 143 19 138 44 C 131 57 141 72 166 78 C 183 84 199 88 207 99 Z','url(#plates)',3,.95),
        ('M -29 190 C 19 174 36 150 56 138 C 71 129 82 119 90 104 '
         'C 99 127 84 164 61 184 L 35 218 Z','#1d1825',0,.79),
        ('M -23 192 C 19 173 41 145 55 140 C 69 135 79 128 87 115 '
         'C 87 144 74 171 58 189 L 35 213 Z','url(#plates)',3,.95),
        ('M 216 190 C 168 176 151 150 134 137 C 115 122 105 117 99 106 '
         'C 96 131 117 171 140 191 L 166 218 Z','#181522',0,.81),
        ('M 211 194 C 167 177 148 147 134 141 C 116 132 109 125 105 119 '
         'C 106 148 122 174 142 192 L 164 215 Z','url(#plates)',3,.95),
    ]
    for d, fill, width, opacity in plates:
        bits.append(path(d,fill=fill,stroke='#241926',width=width,op=opacity))
    ribs = [
        ('M -4 -6 C 31 7 58 20 70 39 C 75 49 63 63 43 69','#cbb0a7',5,.78),
        ('M 0 16 C 22 28 40 34 53 50','#e4ccaf',2,.60),
        ('M -4 87 C 22 91 43 89 60 79','#261c2d',4,.78),
        ('M 195 5 C 174 17 156 29 152 45 C 151 56 163 60 179 65','#ceb4a9',5,.72),
        ('M 194 18 C 176 28 168 34 164 46','#e6c6ac',2,.55),
        ('M -8 189 C 23 175 43 153 55 143 C 67 142 72 135 78 128','#b79da0',4,.76),
        ('M 10 190 C 30 177 49 159 57 154','#e2c3a7',2,.56),
        ('M 201 192 C 167 176 151 157 135 143 C 124 138 120 132 114 127','#b8a0a1',4,.75),
        ('M 188 188 C 164 175 148 156 137 151','#e4c3ad',2,.51),
        ('M 78 62 C 90 72 94 83 95 103 C 96 120 87 130 77 146 '
         'M 115 66 C 104 77 99 88 99 106 C 101 122 110 133 119 144','#211825',9,.68),
        ('M 78 65 C 89 80 92 91 92 102 S 86 128 78 139 '
         'M 115 69 C 105 86 102 91 102 104 S 107 126 117 139','#b09ba2',3.4,.58),
        ('M 80 74 C 92 84 97 96 97 104 C 97 120 88 127 85 132','#d2b0a8',1.5,.55),
    ]
    for d,stroke,width,opacity in ribs:
        bits.append(path(d,stroke=stroke,width=width,op=opacity))
    bits.append(path('M 83 35 C 97 52 99 61 100 83 C 102 101 95 111 98 129 '
                     'C 101 151 116 161 114 189 C 108 164 88 153 89 126 '
                     'C 91 108 88 102 87 86 C 87 66 79 50 83 35 Z',
                     fill='#1d1724',stroke='#251a28',width=3,op=.80))
    bits.append(path('M 90 40 C 100 59 99 76 98 90 C 97 108 94 114 96 132 '
                     'C 98 149 106 160 107 174 C 96 155 92 143 92 124 '
                     'C 93 106 93 93 93 80 C 92 61 86 53 90 40 Z',
                     fill='url(#carapace)',op=.82))
    bits.append(path('M 93 61 C 96 82 94 106 95 115 C 95 136 100 143 102 151',
                     stroke='#e0cab4',width=2,op=.64))
    # Light marks the rare living junction rather than every 1x1 repeat.
    if mask not in (1,2,4,5,8,10):
        bits.append('<ellipse cx="95" cy="107" rx="15" ry="29" fill="url(#eye)" opacity=".68"/>')
        bits.append(path('M 95 90 Q 101 104 95 120 Q 89 104 95 90 Z',
                         fill='#80aa83',stroke='#315c58',width=1.3,op=.77))
        bits.append(path('M 95 94 L 95 113',stroke='#c7d5a2',width=1.5,op=.66))
        for d in ('M 72 89 Q 83 94 88 108','M 120 89 Q 106 98 104 111',
                  'M 68 119 Q 79 115 86 121','M 130 118 Q 117 112 107 119'):
            bits.append(path(d,stroke='#88a688',width=1.7,op=.34))
    for d in ('M 34 117 l 13 -6 l 11 3','M 149 119 l 13 3 l 8 -8',
              'M 63 44 l 8 4 l 4 -6','M 143 162 l -7 -3 l -6 7'):
        bits.append(path(d,stroke='#201923',width=4,op=.55))
        bits.append(path(d,stroke='#af999e',width=1.35,op=.39))
    return bits
