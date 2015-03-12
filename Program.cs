using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace ResizeTo3MB
{
	class Program
	{
		static void Main(string[] args)
		{
			List<string> lst = new List<string>();

			if (args.Length > 0)
			{
				lst.AddRange(args.Where<string>(s => s.EndsWith(".jpg") | s.EndsWith(".png") | s.EndsWith(".bmp")));
			}
			else
			{
				lst.AddRange(Directory.GetFiles("./", "*.jpg", SearchOption.TopDirectoryOnly));
				lst.AddRange(Directory.GetFiles("./", "*.png", SearchOption.TopDirectoryOnly));
				lst.AddRange(Directory.GetFiles("./", "*.bmp", SearchOption.TopDirectoryOnly));
			}

			long	i = 0;
			object	sync = new object();

			Parallel.ForEach<string>(
				lst,
				(s) =>
				{
					try
					{
						Data data = ResizeImage(s);
						Interlocked.Increment(ref i);

						lock (sync)
						{
							if (data.Resized)
								Console.ForegroundColor = ConsoleColor.Yellow;
							else
								Console.ForegroundColor = ConsoleColor.White;

							Console.WriteLine(
								"[{0} / {1}] {2}x{3} {4} => {5}x{6} {7} : {8}",
								i,
								lst.Count,
								data.SizeO.Width, data.SizeO.Height,
								ToSize(data.CapO),
								data.SizeR.Width, data.SizeR.Height,
								ToSize(data.CapR),
								Path.GetFileName(s));
						}
					}
					catch
					{ }
				});

			Console.WriteLine();
			Console.WriteLine("DONE.");
			Console.ReadKey();
		}

		private const int MaxSize = 2883584;	// 약 2.75 MB

		class Data
		{
			public bool		Resized;

			public Image	Image;
			public byte[]	Bytes;

			public Size		SizeO;
			public Size		SizeR;

			public long		CapO;
			public long		CapR;
		}

		private static Data ResizeImage(string path)
		{
			Data data = new Data();

			data.Bytes	= File.ReadAllBytes(path);
			using (MemoryStream stream = new MemoryStream(data.Bytes))
			using (data.Image = Image.FromStream(stream))
			{
				data.SizeO	= data.Image.Size;
				data.CapO	= data.Bytes.Length;

				if (data.Bytes.Length < MaxSize)
				{
					data.SizeR = data.SizeO;
					data.CapR = data.CapO;
				}
				else
				{
					data.Resized = true;

					// not available GIF now.
					ImageCodecInfo		codec;
					EncoderParameters	param;

					if (data.Image.RawFormat.Guid == ImageFormat.Jpeg.Guid || !IsImageTransparent(data.Image))
					{
						codec = ImageCodecInfo.GetImageDecoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
						param = new EncoderParameters(1);

						long quality = 90;
						if (data.Image.PropertyIdList.Any(e => e == 0x5010)) quality = data.Image.PropertyItems.First(e => e.Id == 0x5010).Value[0];

						param.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

						ResizeJpg(data, codec, param);
					}
					else
					{
						codec = ImageCodecInfo.GetImageDecoders().First(e => e.FormatID == ImageFormat.Png.Guid);
						param = new EncoderParameters(1);
						param.Param[0] = new EncoderParameter(Encoder.ColorDepth, Bitmap.GetPixelFormatSize(data.Image.PixelFormat));

						ResizePng(data, codec, param);
					}

					if (!Directory.Exists("Backup")) Directory.CreateDirectory("Backup");
					File.Move(path, Path.Combine(Path.Combine(Path.GetDirectoryName(path), "Backup"), Path.GetFileName(path)));

				}
			}

			if (data.Resized)
				File.WriteAllBytes(path, data.Bytes);


			return data;
		}

		private static void ResizeJpg(Data data, ImageCodecInfo codec, EncoderParameters param)
		{
			int w = data.Image.Width;
			int h = data.Image.Height;

			do
			{
				ResizeBySize(data, codec, param, w, h);

				w = (int)(w * 0.9f);
				h = (int)(h * 0.9f);
			}
			while (data.Bytes.Length > MaxSize);
		}

		private static void ResizePng(Data data, ImageCodecInfo codec, EncoderParameters param)
		{
			int w, h;

			GetSizeFromPixels(MaxSize * param.Param[0].NumberOfValues / 8 * 2, data.Image.Width, data.Image.Height, out w, out h);

			do
			{
				ResizeBySize(data, codec, param, w, h);

				w = (int)(w * 0.9f);
				h = (int)(h * 0.9f);
			}
			while (data.Bytes.Length > MaxSize);
		}
		
		private static void GetSizeFromPixels(int pixels, int oriW, int oriH, out int newW, out int newH)
		{
			newW = (int)Math.Ceiling(Math.Sqrt(pixels * oriW / oriH));
			newH = (int)Math.Ceiling(Math.Sqrt(pixels * oriH / oriW));

			if (newW > oriW) newW = oriW;
			if (newH > oriH) newH = oriH;
		}

		private static void ResizeBySize(Data data, ImageCodecInfo codec, EncoderParameters param, int w, int h)
		{
			using (Image imageNew = new Bitmap(w, h, data.Image.PixelFormat))
			{
				using (Graphics g = Graphics.FromImage(imageNew))
				{
					foreach (PropertyItem propertyItem in data.Image.PropertyItems)
						imageNew.SetPropertyItem(propertyItem);

					g.InterpolationMode = InterpolationMode.HighQualityBicubic;
					g.PixelOffsetMode = PixelOffsetMode.HighQuality;
					g.SmoothingMode = SmoothingMode.AntiAlias;

					g.DrawImage(data.Image, 0, 0, w, h);
				}

				using (MemoryStream memStream = new MemoryStream())
				{
					imageNew.Save(memStream, codec, param);
					data.Bytes = memStream.ToArray();

					data.SizeR = imageNew.Size;
					data.CapR = data.Bytes.Length;
				}
			}
		}

		private static bool IsImageTransparent(Image image)
		{
			PixelFormat[] formatsWithAlpha =
			{
				PixelFormat.Indexed,				PixelFormat.Gdi,				PixelFormat.Alpha,
				PixelFormat.PAlpha,					PixelFormat.Canonical,			PixelFormat.Format1bppIndexed,
				PixelFormat.Format4bppIndexed,		PixelFormat.Format8bppIndexed,	PixelFormat.Format16bppArgb1555,
				PixelFormat.Format32bppArgb,		PixelFormat.Format32bppPArgb,	PixelFormat.Format64bppArgb,
				PixelFormat.Format64bppPArgb
			};

			bool isTransparent = false;

			if (formatsWithAlpha.Contains(image.PixelFormat))
			{
				Bitmap bitmap = image as Bitmap;
				BitmapData binaryImage = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format64bppArgb);

				unsafe
				{
					byte* pointerToImageData = (byte*)binaryImage.Scan0;
					int numberOfPixels = bitmap.Width * bitmap.Height;

					// 8 bytes = 64 bits, since our image is 64bppArgb.
					for (int i = 0; i < numberOfPixels * 8; i += 8)
					{
						// Check the last two bytes (transparency channel). First six bytes are for R, G and B channels. (0, 32) means 100% opacity.
						if (pointerToImageData[i + 6] != 0 || pointerToImageData[i + 7] != 32)
						{
							isTransparent = true;
							break;
						}
					}
				}

				bitmap.UnlockBits(binaryImage);
			}

			return isTransparent;
		}

		private static string ToSize(long size)
		{
			if (size < 1000)
				return string.Format("{0:##0.0} B", size);
			else if (size < 1024000)
				return string.Format("{0:##0.0} KiB", size / 1024d);
			else if (size < 1048576000)
				return string.Format("{0:##0.0} MiB", size / 1048576d);
			else
				return string.Format("{0:##0.0} GiB", size / 1073741824d);

		}
	}
}
