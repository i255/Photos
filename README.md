# μPhotos

μPhotos is an open source GPU-accelerated cross platform immersive photo viewer and organizer. It is written in C# and uses the Skia 2D graphic library for drawing.

[Project website](https://www.bytificial.com/uPhotos)

## Features

μPhotos was inspired by the [Picasa photo viewer](https://en.wikipedia.org/wiki/Picasa). I could not find some of the Picasa features in other picture viewers, so I decided to write my own. Here is a list of features, that make μPhotos special:

- __Performance__: μPhotos is very fast. My library has around 100k photos from the last 20 years. 
  - μPhotos can scroll through 100k thumbnails in a second with 60fps. 
  - Full screen photos are loaded instantly, so skipping 20 images per second with arrow keys is possible.
  - Short application startup time. It is around 1 second on my laptop.
- __Immersiveness__: μPhotos blends out all the windows, buttons and borders to concentrate on the photos.
- __Cloudless__: no need to upload the pictures into a cloud.
- __Filters__: filter your photo library by year, folder name, country, nearest city or camera model. Useful when trying to find a particular photo.
- __Synchronization__: sync the high resolution thumbnails to your mobile device via Wi-Fi. Required storage space is about 2% of what is required for the full size photos (20GB instead of 1TB in my case). The quality is sufficient to view full screen pictures on a mobile device. Full size images can be viewed when you are in Wi-Fi range. The connection is encrypted using the AES GCM algorithm.
- __Supported image formats__: `.jpg`, `.jpeg`, `.gif`, `.webp`, `.heic`, `.png`, `.bmp`. The desktop version also supports `.tif`, `.tiff`, `.svg`.

## License

μPhotos is licensed under the [MIT](LICENSE.TXT) license.

