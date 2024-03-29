﻿using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ArcFaceComponentNuget
{
    public class ArcFaceComponent : IDisposable
    {
        private const string modelPath = "ArcFaceComponent.arcfaceresnet100-8.onnx";
        private InferenceSession session;

        public ArcFaceComponent()
        {
            using var modelStream = typeof(ArcFaceComponent).Assembly.GetManifestResourceStream(modelPath);
            ArgumentNullException.ThrowIfNull(modelStream);

            using var memoryStream = new MemoryStream();
            modelStream?.CopyTo(memoryStream);

            session = new InferenceSession(memoryStream.ToArray());
            ArgumentNullException.ThrowIfNull(session);
        }

        public void Dispose() => session?.Dispose();

        /// <summary>
        /// Method gets images and calculates distance between every two images.
        /// </summary>
        /// <returns>
        /// Distance matrix.
        /// </returns>
        public async Task<float[,]> GetDistanceMatrix(Image<Rgb24>[] images, CancellationToken token)
        {
            float[,] distanceMatrix = new float[images.Length, images.Length];

            try
            {
                CheckToken(token);
                var tasks = new List<Task<float[]>>();
                Array.ForEach(images, image => tasks.Add(GetEmbeddings(image, token)));
                var embeddings = await Task.WhenAll(tasks);

                int i = 0;

                foreach (var emb1 in embeddings)
                {
                    int j = 0;
                    foreach (var emb2 in embeddings)
                    {
                        distanceMatrix[i, j] = Distance(emb1, emb2);
                        j++;
                    }
                    i++;
                }

                return distanceMatrix;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Method gets images and calculates similarity between every two images.
        /// </summary>
        /// <returns>
        /// Similarity matrix.
        /// </returns>
        public async Task<float[,]> GetSimilarityMatrix(Image<Rgb24>[] images, CancellationToken token)
        {
            float[,] similarityMatrix = new float[images.Length, images.Length];

            try
            {
                CheckToken(token);
                var tasks = new List<Task<float[]>>();
                Array.ForEach(images, image => tasks.Add(GetEmbeddings(image, token)));
                var embeddings = await Task.WhenAll(tasks);

                int i = 0;
                foreach (var iemb in embeddings)
                {
                    int j = 0;
                    foreach (var iemb2 in embeddings)
                    {
                        similarityMatrix[i, j] = Similarity(iemb, iemb2);
                        j++;
                    }
                    i++;
                }

                return similarityMatrix;
            }
            catch
            {
                return null;
            }
        }

        private static DenseTensor<float> ImageToTensor(Image<Rgb24> image)
        {
            ArgumentNullException.ThrowIfNull(image);

            var tensor = new DenseTensor<float>(new[] { 1, 3, image.Height, image.Width });

            image.ProcessPixelRows(pixel =>
            {
                for (int y = 0; y < image.Height; y++)
                {
                    Span<Rgb24> pixelSpan = pixel.GetRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        tensor[0, 0, y, x] = pixelSpan[x].R;
                        tensor[0, 1, y, x] = pixelSpan[x].G;
                        tensor[0, 2, y, x] = pixelSpan[x].B;
                    }
                }
            });

            return tensor;
        }

        private static void CheckToken(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }
        }

        public static float Distance(float[] v1, float[] v2) => Length(v1.Zip(v2).Select(p => p.First - p.Second).ToArray());

        public async Task<float[]> GetEmbeddings(Image<Rgb24> image, CancellationToken token)
        {
            return await Task<float[]>.Factory.StartNew(() =>
            {
                CheckToken(token);

                var data = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("data", ImageToTensor(image)) };

                CheckToken(token);

                lock (session)
                {
                    using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(data);
                    return Normalize(results.First(v => v.Name == "fc1").AsEnumerable<float>().ToArray());
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private static float Length(float[] v) => (float)Math.Sqrt(v.Select(x => x * x).Sum());

        private static float[] Normalize(float[] vectors) => vectors.Select(x => x / Length(vectors)).ToArray();

        public static float Similarity(float[] firstVector, float[] secondVector) => firstVector.Zip(secondVector).Select(p => p.First * p.Second).Sum();
    }
}