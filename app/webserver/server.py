import socket
import json
import logging
import requests
import os
import time

# Import EasyOCR implementation
from process_image_easyocr import process_image as process_image_easyocr
# Import Manga OCR implementation
from process_image_mangaocr import process_image as process_image_mangaocr
# Import docTR implementation
from process_image_doctr import process_image as process_image_doctr

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

# Server configuration
HOST = '127.0.0.1'  # Standard loopback interface address (localhost) (change to 0.0.0.0 to be remotely accessible)
PORT = 9999         # Port to listen on (non-privileged ports are > 1023)
BUFFER_SIZE = 1024  # Buffer size for receiving data

def handle_client_connection(conn, addr):
    """
    Handle a client connection.
    
    Args:
        conn: The client connection socket
        addr: The client address
    """
    logger.info(f"Connected by {addr}")
    
    try:
        while True:
            # Receive data from the client
            data = conn.recv(BUFFER_SIZE)
            
            # If no data, the client has closed the connection
            if not data:
                logger.info(f"Client {addr} disconnected")
                break
            
            # Decode and process the command
            command = data.decode('utf-8').strip()
            logger.info(f"Received command: {command}")
            
            if command.startswith("read_image"):
                # Start timing the process
                start_time = time.time()
                
                # Parse parameters if provided
                lang = 'japan'  # Default language
                implementation = 'easyocr'  # Default to EasyOCR
                min_region_width = 10  # Default minimum region width
                min_region_height = 10  # Default minimum region height
                overlap_allowed_percent = 90.0  # Default overlap allowed percentage
                
                if "|" in command:
                    parts = command.split("|")
                    if len(parts) > 1 and parts[1]:
                        lang = parts[1]
                    # Parse implementation parameter
                    if len(parts) > 2 and parts[2]:
                        implementation = parts[2].lower()
                    # Parse min_region_width, min_region_height, and overlap_allowed_percent parameters (for Manga OCR)
                    # Format: read_image|lang|implementation|char_level|min_width|min_height|overlap_percent
                    # parts[3] is "char_level", parts[4] is min_width, parts[5] is min_height, parts[6] is overlap_percent
                    if implementation == 'mangaocr' or implementation == 'manga-ocr' or implementation == 'manga_ocr':
                        if len(parts) > 4 and parts[4]:
                            try:
                                min_region_width = int(parts[4])
                            except ValueError:
                                logger.warning(f"Invalid min_region_width: {parts[4]}, using default: {min_region_width}")
                        if len(parts) > 5 and parts[5]:
                            try:
                                min_region_height = int(parts[5])
                            except ValueError:
                                logger.warning(f"Invalid min_region_height: {parts[5]}, using default: {min_region_height}")
                        if len(parts) > 6 and parts[6]:
                            try:
                                overlap_allowed_percent = float(parts[6])
                            except ValueError:
                                logger.warning(f"Invalid overlap_allowed_percent: {parts[6]}, using default: {overlap_allowed_percent}")
                
                # Check if character-level OCR is requested
                char_level = True  # Default to character-level
                
                # Process image based on the selected implementation
                if implementation == 'mangaocr' or implementation == 'manga-ocr' or implementation == 'manga_ocr':
                    # Log the OCR engine being used
                    logger.info(f"Using Manga OCR with language: {lang}, character-level: {char_level}, min_region: {min_region_width}x{min_region_height}, overlap_allowed: {overlap_allowed_percent}%")
                    # Process image with Manga OCR
                    result = process_image_mangaocr("image_to_process.png", lang=lang, char_level=char_level,
                                                    min_region_width=min_region_width, min_region_height=min_region_height,
                                                    overlap_allowed_percent=overlap_allowed_percent)
                elif implementation == 'doctr' or implementation == 'doctr-ocr' or implementation == 'doctr_ocr':
                    # Log the OCR engine being used
                    logger.info(f"Using docTR with language: {lang}, character-level: {char_level}")
                    # Process image with docTR
                    result = process_image_doctr("image_to_process.png", lang=lang, char_level=char_level)
                else:
                    # Default to EasyOCR
                    # Log the OCR engine and language being used
                    logger.info(f"Using EasyOCR with language: {lang}, character-level: {char_level}")
                    # Process image with EasyOCR
                    result = process_image_easyocr("image_to_process.png", lang=lang, char_level=char_level)
                
                # Send results back to client as JSON
                # Set ensure_ascii=False to keep Japanese characters as they are
                response = json.dumps(result, ensure_ascii=False).encode('utf-8')
                
                # First send the size of the response
                response_size = len(response)
                size_header = str(response_size).encode('utf-8') + b'\r\n'
                
                # Clear socket buffers before sending
                conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                
                # Send size header
                conn.sendall(size_header)
                
                # Small delay to ensure header and data don't get merged
                time.sleep(0.01)
                
                # Then send the actual response
                conn.sendall(response)
                
                # Calculate time taken and log it
                time_taken = time.time() - start_time
                logger.info(f"Sent OCR results to client (time taken: {time_taken:.2f} seconds)")
            else:
                # Unknown command
                error_msg = json.dumps({"status": "error", "message": "Unknown command"}).encode('utf-8')
                conn.sendall(str(len(error_msg)).encode('utf-8') + b'\r\n')
                conn.sendall(error_msg)
                logger.info(f"Unknown command: {command}")
    
    except Exception as e:
        logger.error(f"Error handling client connection: {e}")
    
    finally:
        # Clean up the connection
        conn.close()
        logger.info(f"Connection with {addr} closed")

def main():
    """Start the server and listen for connections."""
    
    # Create a TCP/IP socket
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        # Set socket option to reuse address
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        
        # Bind the socket to the address and port
        s.bind((HOST, PORT))
        
        # Listen for incoming connections (5 is the backlog size)
        s.listen(5)
        
        logger.info(f"Server started on {HOST}:{PORT}")
        
        try:
            while True:
                # Wait for a connection
                conn, addr = s.accept()
                
                # Handle the connection
                handle_client_connection(conn, addr)
        
        except KeyboardInterrupt:
            logger.info("Server shutting down...")
        
        except Exception as e:
            logger.error(f"Error in main server loop: {e}")

if __name__ == "__main__":
    main()
