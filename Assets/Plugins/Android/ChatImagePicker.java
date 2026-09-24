package com.virtualeducation.chat;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.ContentUris;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.net.Uri;
import android.os.Build;
import android.provider.MediaStore;
import android.provider.OpenableColumns;
import android.util.Size;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;

/** Bridge for the image grid rendered directly inside ChatAIScene. */
public final class ChatImagePicker {
    private ChatImagePicker() {}

    public static String getRecentImages(Activity activity, int requestedLimit) {
        return getRecentImagesPage(activity, 0, requestedLimit);
    }

    /** Returns one page ordered from newest to oldest. */
    public static String getRecentImagesPage(
            Activity activity,
            int requestedOffset,
            int requestedLimit) {
        JSONObject result = new JSONObject();
        JSONArray items = new JSONArray();
        Cursor cursor = null;

        try {
            int offset = Math.max(0, requestedOffset);
            int limit = Math.max(1, Math.min(requestedLimit, 40));
            ContentResolver resolver = activity.getContentResolver();
            Uri collection = MediaStore.Images.Media.EXTERNAL_CONTENT_URI;
            String[] projection = {
                MediaStore.Images.Media._ID,
                MediaStore.Images.Media.DISPLAY_NAME
            };

            cursor = resolver.query(
                collection,
                projection,
                null,
                null,
                MediaStore.Images.Media.DATE_ADDED + " DESC"
            );

            if (cursor != null) {
                int idColumn = cursor.getColumnIndexOrThrow(MediaStore.Images.Media._ID);
                int nameColumn = cursor.getColumnIndex(MediaStore.Images.Media.DISPLAY_NAME);
                File directory = new File(activity.getCacheDir(), "chat_gallery_thumbnails");
                if (!directory.exists()) directory.mkdirs();

                int scanned = 0;
                int count = 0;
                if (offset > 0) cursor.moveToPosition(offset - 1);
                while (cursor.moveToNext() && count < limit) {
                    scanned++;
                    long id = cursor.getLong(idColumn);
                    Uri uri = ContentUris.withAppendedId(collection, id);
                    String name = nameColumn >= 0 ? cursor.getString(nameColumn) : null;
                    if (name == null || name.trim().isEmpty()) name = "image_" + id + ".jpg";

                    File thumbnail = new File(directory, "thumb_" + id + ".png");
                    if (!createThumbnail(resolver, uri, thumbnail)) continue;

                    JSONObject item = new JSONObject();
                    item.put("uri", uri.toString());
                    item.put("displayName", name);
                    item.put("thumbnailPath", thumbnail.getAbsolutePath());
                    items.put(item);
                    count++;
                }

                int nextOffset = offset + scanned;
                result.put("nextOffset", nextOffset);
                result.put("hasMore", nextOffset < cursor.getCount());
            }

            result.put("items", items);
            result.put("error", "");
        } catch (Exception exception) {
            try {
                result.put("items", items);
                result.put("error", exception.getMessage() == null
                    ? exception.toString() : exception.getMessage());
            } catch (Exception ignored) {}
        } finally {
            if (cursor != null) cursor.close();
        }

        return result.toString();
    }

    public static String copyImageToCache(
            Activity activity,
            String uriValue,
            String suggestedName) {
        if (uriValue == null || uriValue.trim().isEmpty()) return "";

        try {
            Uri uri = Uri.parse(uriValue);
            String name = suggestedName;
            if (name == null || name.trim().isEmpty()) name = resolveFileName(activity, uri);
            if (name == null || name.trim().isEmpty())
                name = "chat_image_" + System.currentTimeMillis() + ".jpg";

            File destination = new File(
                activity.getCacheDir(),
                "chat_" + System.currentTimeMillis() + "_" + sanitizeFileName(name)
            );
            copyUriToFile(activity, uri, destination);
            return destination.getAbsolutePath();
        } catch (Exception exception) {
            exception.printStackTrace();
            return "";
        }
    }

    private static boolean createThumbnail(
            ContentResolver resolver,
            Uri uri,
            File destination) {
        try {
            Bitmap bitmap;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                bitmap = resolver.loadThumbnail(uri, new Size(320, 320), null);
            } else {
                bitmap = MediaStore.Images.Thumbnails.getThumbnail(
                    resolver,
                    ContentUris.parseId(uri),
                    MediaStore.Images.Thumbnails.MINI_KIND,
                    null
                );
            }

            if (bitmap == null) return false;

            // MediaStore can return a GPU/hardware-backed bitmap on newer
            // Android devices. Compressing that bitmap directly produces
            // solid-black thumbnails on some vendors, so always render it
            // into a normal software ARGB bitmap first.
            Bitmap softwareBitmap = Bitmap.createBitmap(
                bitmap.getWidth(),
                bitmap.getHeight(),
                Bitmap.Config.ARGB_8888
            );
            Canvas canvas = new Canvas(softwareBitmap);
            canvas.drawBitmap(bitmap, 0f, 0f, null);

            try (FileOutputStream output = new FileOutputStream(destination)) {
                boolean success = softwareBitmap.compress(Bitmap.CompressFormat.PNG, 100, output);
                output.flush();
                return success;
            } finally {
                softwareBitmap.recycle();
                bitmap.recycle();
            }
        } catch (Exception exception) {
            exception.printStackTrace();
            return false;
        }
    }

    private static String resolveFileName(Activity activity, Uri uri) {
        Cursor cursor = null;
        try {
            cursor = activity.getContentResolver().query(
                uri,
                new String[] { OpenableColumns.DISPLAY_NAME },
                null, null, null
            );
            if (cursor != null && cursor.moveToFirst()) {
                int index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                if (index >= 0) return cursor.getString(index);
            }
        } finally {
            if (cursor != null) cursor.close();
        }
        return null;
    }

    private static void copyUriToFile(
            Activity activity,
            Uri uri,
            File destination) throws Exception {
        try (InputStream input = activity.getContentResolver().openInputStream(uri);
             FileOutputStream output = new FileOutputStream(destination)) {
            if (input == null)
                throw new IllegalStateException("Unable to open selected image.");

            byte[] buffer = new byte[16 * 1024];
            int read;
            while ((read = input.read(buffer)) > 0) output.write(buffer, 0, read);
            output.flush();
        }
    }

    private static String sanitizeFileName(String value) {
        return value.replaceAll("[^A-Za-z0-9._-]", "_");
    }
}
