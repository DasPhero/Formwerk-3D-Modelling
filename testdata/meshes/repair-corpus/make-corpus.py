"""Run with: python make-corpus.py. Writes the repair corpus (damaged meshes the repair is meant to fix, and two known limits) as binary STL files."""
import math
import os
import random
import struct

root = os.path.dirname(os.path.abspath(__file__))  # the files are written next to this script
limits = os.path.join(root, 'known-limits')
os.makedirs(limits, exist_ok=True)


def volume(tris):
    total = 0.0
    for a, b, c in tris:
        total += (a[0] * (b[1] * c[2] - b[2] * c[1]) - a[1] * (b[0] * c[2] - b[2] * c[0]) + a[2] * (b[0] * c[1] - b[1] * c[0])) / 6
    return total


def outward(tris):
    return tris if volume(tris) >= 0 else [(a, c, b) for a, b, c in tris]


def box(x0, y0, z0, x1, y1, z1):
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    f = [(0, 2, 1), (0, 3, 2), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7)]
    return outward([(v[a], v[b], v[c]) for a, b, c in f])


def cylinder(cx, cy, z0, radius, height, n=32):
    ring = [(cx + radius * math.cos(2 * math.pi * i / n), cy + radius * math.sin(2 * math.pi * i / n)) for i in range(n)]
    tris = []
    for i in range(n):
        j = (i + 1) % n
        a0, a1 = (*ring[i], z0), (*ring[j], z0)
        b0, b1 = (*ring[i], z0 + height), (*ring[j], z0 + height)
        tris += [(a0, a1, b1), (a0, b1, b0)]
        tris.append(((cx, cy, z0), a1, a0))
        tris.append(((cx, cy, z0 + height), b0, b1))
    return outward(tris)


def sphere(cx, cy, cz, radius, lon=24, lat=12):
    def p(i, j):
        theta = math.pi * j / lat
        phi = 2 * math.pi * i / lon
        return (cx + radius * math.sin(theta) * math.cos(phi), cy + radius * math.sin(theta) * math.sin(phi), cz + radius * math.cos(theta))

    tris = []
    for j in range(lat):
        for i in range(lon):
            i2 = (i + 1) % lon
            a, b, c, d = p(i, j), p(i2, j), p(i2, j + 1), p(i, j + 1)
            if j == 0:
                tris.append((a, c, d))
            elif j == lat - 1:
                tris.append((a, b, c))
            else:
                tris += [(a, b, c), (a, c, d)]
    return outward(tris)


def write(folder, name, tris):
    with open(os.path.join(folder, name), 'wb') as file:
        file.write(b'Formwerk repair corpus'.ljust(80, b' '))
        file.write(struct.pack('<I', len(tris)))
        for a, b, c in tris:
            file.write(struct.pack('<3f', 0, 0, 0))
            for p in (a, b, c):
                file.write(struct.pack('<3f', *p))
            file.write(struct.pack('<H', 0))


def without(tris, *indices):
    return [t for i, t in enumerate(tris) if i not in indices]


def flipped(tris, *indices):
    return [(t[0], t[2], t[1]) if i in indices else t for i, t in enumerate(tris)]


def jittered(tris, amount, seed=1):
    rnd = random.Random(seed)
    return [tuple(tuple(c + rnd.uniform(-amount, amount) for c in p) for p in t) for t in tris]


b = box(0, 0, 0, 20, 20, 20)
cyl = cylinder(10, 10, 0, 10, 30)
sph = sphere(10, 10, 10, 10)

write(root, '01-box-hole.stl', without(b, 0))
write(root, '02-box-face-missing.stl', without(b, 0, 1))
write(root, '03-box-one-triangle-turned.stl', flipped(b, 3))
write(root, '04-box-inside-out.stl', flipped(b, *range(len(b))))
write(root, '05-box-doubled-triangle.stl', b + [b[5]])
write(root, '06-box-degenerate-triangle.stl', b + [((1, 1, 1), (1, 1, 1), (5, 5, 5))])
write(root, '07-box-speck.stl', b + box(60, 60, 60, 60.3, 60.3, 60.3))
write(root, '08-box-unwelded-with-noise.stl', jittered(b, 1e-4))
write(root, '09-cylinder-lid-missing.stl', [t for t in cyl if not all(abs(p[2] - 30) < 1e-6 for p in t)])
write(root, '10-cylinder-hole-in-the-wall.stl', without(cyl, 2, 3))
write(root, '11-sphere-hole.stl', without(sph, 10, 11, 12, 30))
write(root, '12-sphere-inside-out.stl', flipped(sph, *range(len(sph))))
write(root, '13-sphere-turned-and-doubled.stl', flipped(sph, 5, 17, 40, 41, 100) + [sph[8], sph[9], sph[200]])
write(root, '14-two-boxes-overlap.stl', b + box(8, 6, 11, 28, 26, 31))
write(root, '15-two-boxes-pushed-along-one-axis.stl', b + box(14, 0, 0, 34, 20, 20))
write(root, '16-box-in-box-sharing-the-floor.stl', box(0, 0, 0, 40, 40, 40) + box(10, 10, 0, 20, 20, 10))
write(root, '17-three-boxes-in-a-row.stl', b + box(14, 3, 2, 34, 23, 22) + box(28, 6, 4, 48, 26, 24))
write(root, '18-cylinder-through-a-box.stl', box(0, 0, 0, 40, 40, 10) + cylinder(19, 21, -5, 6, 20))
write(root, '19-box-with-hole-overlapping-another.stl', without(b, 0) + box(8, 6, 11, 28, 26, 31))
write(root, '20-sphere-and-box-overlap-both-damaged.stl', flipped(sph, *range(len(sph))) + without(box(5, 5, 14, 25, 25, 34), 2))

# known limits: reported, not repaired
write(limits, 'bow-tie.stl', box(0, 0, 0, 10, 10, 10) + box(10, 10, 10, 20, 20, 20))
folded = list(b)
# pull one corner (0,0,0) of the box through the body: every triangle with that corner moves it out through the far corner
folded = [tuple((26.0, 24.0, 27.0) if p == (0, 0, 0) else p for p in t) for t in b]
write(limits, 'folded-box.stl', folded)
print(len(os.listdir(root)) - 1, 'corpus files,', len(os.listdir(limits)), 'known limits')
