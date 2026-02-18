using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace SpotifyOnScreen.Services;

public static class AlbumColorExtractor
{
    public static List<Color> ExtractColors(BitmapImage bitmap, int colorCount = 5)
    {
        var pixels = GetPixels(bitmap);
        if (pixels.Count == 0)
            return [Color.FromRgb(30, 30, 46)];

        return KMeansClustering(pixels, colorCount);
    }

    private static List<Color> GetPixels(BitmapImage bitmap)
    {
        var pixels = new List<Color>();

        var formatted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int width = formatted.PixelWidth;
        int height = formatted.PixelHeight;
        int stride = width * 4;
        var data = new byte[stride * height];
        formatted.CopyPixels(data, stride, 0);

        // Sample every 8th pixel to keep it fast
        int step = 8;
        for (int y = 0; y < height; y += step)
        {
            for (int x = 0; x < width; x += step)
            {
                int i = (y * stride) + (x * 4);
                byte b = data[i];
                byte g = data[i + 1];
                byte r = data[i + 2];
                byte a = data[i + 3];

                if (a < 128) continue;

                // Skip very dark or very light pixels
                float brightness = (r + g + b) / (3f * 255f);
                if (brightness < 0.05f || brightness > 0.95f) continue;

                pixels.Add(Color.FromRgb(r, g, b));
            }
        }

        return pixels;
    }

    private static List<Color> KMeansClustering(List<Color> pixels, int k)
    {
        if (pixels.Count < k)
            k = Math.Max(1, pixels.Count);

        var random = new Random(42);
        var centroids = new List<(double R, double G, double B)>();

        // Initialize centroids from random pixels
        var indices = Enumerable.Range(0, pixels.Count).OrderBy(_ => random.Next()).Take(k).ToList();
        foreach (var idx in indices)
            centroids.Add((pixels[idx].R, pixels[idx].G, pixels[idx].B));

        var assignments = new int[pixels.Count];

        for (int iter = 0; iter < 15; iter++)
        {
            // Assign pixels to nearest centroid
            for (int i = 0; i < pixels.Count; i++)
            {
                double minDist = double.MaxValue;
                int nearest = 0;
                for (int c = 0; c < centroids.Count; c++)
                {
                    double dr = pixels[i].R - centroids[c].R;
                    double dg = pixels[i].G - centroids[c].G;
                    double db = pixels[i].B - centroids[c].B;
                    double dist = dr * dr + dg * dg + db * db;
                    if (dist < minDist) { minDist = dist; nearest = c; }
                }
                assignments[i] = nearest;
            }

            // Update centroids
            var newCentroids = new List<(double R, double G, double B)>();
            for (int c = 0; c < centroids.Count; c++)
            {
                double sumR = 0, sumG = 0, sumB = 0;
                int count = 0;
                for (int i = 0; i < pixels.Count; i++)
                {
                    if (assignments[i] == c)
                    {
                        sumR += pixels[i].R;
                        sumG += pixels[i].G;
                        sumB += pixels[i].B;
                        count++;
                    }
                }
                if (count > 0)
                    newCentroids.Add((sumR / count, sumG / count, sumB / count));
                else
                    newCentroids.Add(centroids[c]);
            }
            centroids = newCentroids;
        }

        // Sort by cluster size (most dominant first)
        var clusterSizes = new int[k];
        for (int i = 0; i < pixels.Count; i++)
            clusterSizes[assignments[i]]++;

        return centroids
            .Select((c, i) => (Color: Color.FromRgb(
                (byte)Math.Clamp(c.R, 0, 255),
                (byte)Math.Clamp(c.G, 0, 255),
                (byte)Math.Clamp(c.B, 0, 255)),
                Size: clusterSizes[i]))
            .OrderByDescending(x => x.Size)
            .Select(x => x.Color)
            .ToList();
    }
}
