package com.virtualeducation.chat;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.provider.OpenableColumns;
import android.database.Cursor;
import android.content.ContentResolver;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;

public final class ChatImagePicker {
    private static final int REQUEST_PICK_IMAGE = 48192;

    private static String receiverObjectName;
    private static String successMethodName;
    private static String cancelMethodName;

    private ChatImagePicker() {}

    public static void openImagePicker(
            final Activity activity,
            final String receiverObject,
            final String successMethod,
            final String cancelMethod) {

        receiverObjectName = receiverObject;
        successMethodName = successMethod;
        cancelMethodName = cancelMethod;

        activity.runOnUiThread(() -> {
            Intent intent = new Intent(
                    activity,
                    ChatImagePickerActivity.class
            );

            activity.startActivity(intent);
        });
    }

    static void sendSuccess(String localPath) {
        if (receiverObjectName == null ||
            successMethodName == null) {
            return;
        }

        UnityPlayer.UnitySendMessage(
                receiverObjectName,
                successMethodName,
                localPath == null ? "" : localPath
        );
    }

    static void sendCancelled() {
        if (receiverObjectName == null ||
            cancelMethodName == null) {
            return;
        }

        UnityPlayer.UnitySendMessage(
                receiverObjectName,
                cancelMethodName,
                ""
        );
    }

    public static class ChatImagePickerActivity extends Activity {
        private boolean pickerOpened;

        @Override
        protected void onCreate(android.os.Bundle savedInstanceState) {
            super.onCreate(savedInstanceState);

            if (savedInstanceState == null) {
                openPicker();
            }
        }

        private void openPicker() {
            pickerOpened = true;

            Intent intent =
                    new Intent(Intent.ACTION_OPEN_DOCUMENT);

            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType("image/*");

            startActivityForResult(
                    intent,
                    REQUEST_PICK_IMAGE
            );
        }

        @Override
        protected void onActivityResult(
                int requestCode,
                int resultCode,
                Intent data) {

            super.onActivityResult(
                    requestCode,
                    resultCode,
                    data
            );

            if (requestCode != REQUEST_PICK_IMAGE) {
                return;
            }

            if (resultCode != RESULT_OK ||
                data == null ||
                data.getData() == null) {

                ChatImagePicker.sendCancelled();
                finish();
                return;
            }

            Uri uri = data.getData();

            try {
                String fileName =
                        resolveFileName(uri);

                if (fileName == null ||
                    fileName.trim().isEmpty()) {

                    fileName =
                            "chat_image_" +
                            System.currentTimeMillis() +
                            ".jpg";
                }

                fileName = sanitizeFileName(fileName);

                File destination =
                        new File(
                                getCacheDir(),
                                "chat_" +
                                System.currentTimeMillis() +
                                "_" +
                                fileName
                        );

                copyUriToFile(
                        uri,
                        destination
                );

                ChatImagePicker.sendSuccess(
                        destination.getAbsolutePath()
                );
            }
            catch (Exception exception) {
                exception.printStackTrace();
                ChatImagePicker.sendCancelled();
            }

            finish();
        }

        @Override
        protected void onResume() {
            super.onResume();

            // When the picker is cancelled Android returns through
            // onActivityResult, so no extra handling is needed here.
        }

        private String resolveFileName(Uri uri) {
            ContentResolver resolver =
                    getContentResolver();

            Cursor cursor = null;

            try {
                cursor = resolver.query(
                        uri,
                        new String[] {
                                OpenableColumns.DISPLAY_NAME
                        },
                        null,
                        null,
                        null
                );

                if (cursor != null &&
                    cursor.moveToFirst()) {

                    int index =
                            cursor.getColumnIndex(
                                    OpenableColumns.DISPLAY_NAME
                            );

                    if (index >= 0)
                        return cursor.getString(index);
                }
            }
            finally {
                if (cursor != null)
                    cursor.close();
            }

            return null;
        }

        private void copyUriToFile(
                Uri uri,
                File destination)
                throws Exception {

            ContentResolver resolver =
                    getContentResolver();

            try (InputStream input =
                         resolver.openInputStream(uri);
                 FileOutputStream output =
                         new FileOutputStream(destination)) {

                if (input == null)
                    throw new IllegalStateException(
                            "Unable to open selected image."
                    );

                byte[] buffer =
                        new byte[16 * 1024];

                int read;

                while ((read = input.read(buffer)) > 0) {
                    output.write(buffer, 0, read);
                }

                output.flush();
            }
        }

        private String sanitizeFileName(String value) {
            return value.replaceAll(
                    "[^A-Za-z0-9._-]",
                    "_"
            );
        }
    }
}
