# Input Counter — Effect Manifest

Semua efek di bawah digambar secara prosedural oleh aplikasi; tidak ada aset game atau gambar eksternal yang digunakan.

## Preview visual

![Atlas preview efek](previews/effect-atlas-v1.png)

Atlas ini adalah referensi visual original untuk kelompok efek, bukan screenshot literal dari tiap frame animasi. Urutannya dari kiri ke kanan, atas ke bawah:

1. gerak cepat/warna biru; 2. tanda getaran; 3. burst biru; 4. sparkle;
5. star trail; 6. critical kuning; 7. critical oranye; 8. critical merah;
9. partikel decay jatuh; 10. rewind cyan; 11. aura rewind hijau; 12. recovery hijau-cyan.

| Nama efek | Kapan terpicu | Visual |
|---|---|---|
| `speed-colour-gradient` | Setiap pembaruan `key/sec` | Warna Total Key dan `key/sec` bergradasi kontinu dari putih ke biru tua. |
| `speed-shake` | `key/sec > 4` | Panel bergetar; amplitudo dan frekuensi naik mengikuti laju input. |
| `total-increase-pop` | Semua input fisik yang menambah total | Angka Total bergerak sedikit ke atas dan mendapat flash putih. |
| `increase-sparkles` | Input normal `+1` atau recovery positif | Sparkle/simbol kecil atau `+1` muncul dan melayang dari sekitar angka Total. |
| `increase-star-trails` | Semua peningkatan Total | Empat partikel bintang dengan jejak cahaya melesat dari area Total. |
| `critical-text` | Input saat `key/sec > 10` dan multiplier hasil chance `+2` sampai `+5` | Teks outline `CRIT +N` melayang pada arah acak jam 12 sampai jam 3. |
| `critical-burst` | Bersamaan dengan `critical-text` | Burst bintang; jumlah burst mengikuti multiplier. |
| `total-decrease-dip` | Tick decay dengan hasil negatif | Angka Total bergerak sedikit ke bawah dan mendapat flash merah-oranye. |
| `decrease-falling-particles` | Tick decay dengan hasil negatif | Tiga partikel biru jatuh dan memudar. |
| `decrease-floating-text` | Tick decay dengan hasil negatif | Floating text `-1` muncul di dekat angka Total dan memudar. |
| `rewind-text` | Chance REWIND berhasil saat idle | Teks cyan/hijau `REWIND!` dengan outline putih melayang. |
| `rewind-burst` | Bersamaan dengan `rewind-text` | Enam burst bintang cyan/hijau. |
| `rewind-green-state` | Setelah REWIND hingga debt decay mencapai 100 | Font Total diblend hijau; hijau memudar kontinu sesuai akumulasi decay. |

## Catatan trigger

- `key/sec` hanya menghitung input fisik dalam satu detik terakhir, bukan multiplier critical.
- Critical dicek pada setiap input fisik saat `key/sec > 10`.
- Decay dimulai setelah tiga detik tanpa input fisik dan berjalan setiap detik.
- REWIND hanya dapat terjadi saat cooldown-nya tidak aktif. Setelah REWIND, cooldown dibuka kembali saat debt decay mencapai 100 atau ada input fisik baru.
