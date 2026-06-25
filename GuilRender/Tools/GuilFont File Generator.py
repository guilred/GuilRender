import pygame as pg
import tkinter as tk
from tkinter import filedialog, messagebox
from tkinter import ttk, scrolledtext
from pathlib import Path
import io
import zipfile

pg.init()
WHITE = (255, 255, 255)
TRANSPARENT = (0, 0, 0, 0)
BLACK = (0, 0, 0)
ATLAS_GAP = 10

def y_cut(surface, background_color=None):
    width, height = surface.get_size()
    top = height
    bottom = 0
    for y in range(height):
        for x in range(width):
            pixel = surface.get_at((x, y))
            if background_color is not None:
                is_content = pixel[:3] != background_color[:3]
            else:
                is_content = pixel.a > 0
            if is_content:
                top = min(top, y)
                bottom = max(bottom, y)
    if bottom < top:
        return pg.Surface((0, 0), pg.SRCALPHA), 0, 0
    crop_height = bottom - top + 1
    cropped = pg.Surface((width, crop_height), pg.SRCALPHA)
    cropped.blit(surface, (0, 0), (0, top, width, crop_height))
    return cropped, top, bottom

def x_cut(surface, background_color=None):
    width, height = surface.get_size()
    left = width
    right = 0
    for y in range(height):
        for x in range(width):
            pixel = surface.get_at((x, y))
            if background_color is not None:
                is_content = pixel[:3] != background_color[:3]
            else:
                is_content = pixel.a > 0
            if is_content:
                left = min(left, x)
                right = max(right, x)
    if right < left:
        return pg.Surface((0, 0), pg.SRCALPHA)
    crop_width = right - left + 1
    cropped = pg.Surface((crop_width, height), pg.SRCALPHA)
    cropped.blit(surface, (0, 0), (left, 0, crop_width, height))
    return cropped

def get_text_surface(font, text, color):
    return font.render(text, True, color)

def generate_atlas_at_size(font_path, size, chars, monospace=False):
    """Generate a single atlas at a specific font size"""
    font = pg.font.Font(font_path, size)
    chars_surfaces = {}
    
    for c in chars:
        sf = get_text_surface(font, c, WHITE)
        chars_surfaces[c] = sf
    
    min_y = float('inf')
    max_y = 0
    valid_chars = []
    
    for c, sf in chars_surfaces.items():
        cropped, top, bottom = y_cut(sf)
        if cropped.get_height() == 0:
            continue
        valid_chars.append(c)
        min_y = min(min_y, top)
        max_y = max(max_y, bottom)
    
    if not valid_chars:
        return None, None, None
    
    cropped_chars_surfaces = {}
    max_char_width = 0
    
    for c in valid_chars:
        sf = chars_surfaces[c]
        cropped = x_cut(sf)
    
        if cropped.get_width() == 0 or cropped.get_height() == 0:
            continue  # skip degenerate surfaces entirely
    
        sub_h = min(max_y - min_y + 1, cropped.get_height() - min_y)  # clamp to surface bounds
        cropped = cropped.subsurface((0, min_y, cropped.get_width(), sub_h)).copy()
        cropped_chars_surfaces[c] = cropped
    
        if cropped.get_width() > max_char_width:
            max_char_width = cropped.get_width()
    
    if monospace:
        char_widths = {c: max_char_width for c in valid_chars}
    else:
        char_widths = {c: cropped_chars_surfaces[c].get_width() for c in valid_chars}
    
    total_width = sum(char_widths[c] for c in valid_chars)
    total_width += ATLAS_GAP * (len(valid_chars) - 1)

    height = max_y - min_y + 1
    
    atlas = pg.Surface((total_width, height), pg.SRCALPHA)
    atlas.fill(TRANSPARENT)
    
    chars_data = ""

    curr_x = 0
    for i, c in enumerate(valid_chars):
        sf = cropped_chars_surfaces[c]
        slot_width = char_widths[c]

        char_x = curr_x
        
        if monospace:
            blit_x = char_x + (slot_width - sf.get_width()) // 2
        else:
            blit_x = char_x

        atlas.blit(sf, (blit_x, 0))
        chars_data += f"{c} {char_x} {slot_width}\n"

        curr_x += slot_width
        if i != len(valid_chars) - 1:
            curr_x += ATLAS_GAP
        
    chars_data = chars_data.rstrip('\n')
    return atlas, chars_data, height

class GuilFontPackerUI:
    def __init__(self):
        self.root = tk.Tk()
        self.root.title("Multi-Resolution GuilFont Packer")
        self.root.geometry("650x550")
        self.font_path = None
        
        ttk.Label(self.root, text="Characters to include:", font=("Arial", 12)).pack(pady=10)
        
        self.text_box = scrolledtext.ScrolledText(self.root, width=70, height=10, wrap=tk.WORD, font=("Arial", 10), undo=True)
        self.text_box.pack(pady=5, padx=20)
        default_chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~¬"
        self.text_box.insert("1.0", default_chars)
        
        # Font sizes section with checkboxes
        size_frame = ttk.LabelFrame(self.root, text="Atlas Sizes (px)", padding=10)
        size_frame.pack(pady=10, padx=20, fill=tk.BOTH, expand=True)
        
        # Common font sizes
        common_sizes = [8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 48, 56, 64, 72, 80, 96, 120]
        self.size_vars = {}
        
        # Create checkboxes in a grid
        checkbox_container = ttk.Frame(size_frame)
        checkbox_container.pack(fill=tk.BOTH, expand=True)
        
        cols = 5
        for i, size in enumerate(common_sizes):
            var = tk.BooleanVar(value=True)  # All checked by default
            self.size_vars[size] = var
            
            cb = ttk.Checkbutton(checkbox_container, text=f"{size}px", variable=var)
            cb.grid(row=i // cols, column=i % cols, sticky=tk.W, padx=5, pady=2)
        
        # Buttons for select all/none and Mono Toggle
        button_container = ttk.Frame(size_frame)
        button_container.pack(pady=5, fill=tk.X)
        
        ttk.Button(button_container, text="Select All", command=lambda: self.toggle_all_sizes(True), width=12).pack(side=tk.LEFT, padx=5)
        ttk.Button(button_container, text="Deselect All", command=lambda: self.toggle_all_sizes(False), width=12).pack(side=tk.LEFT, padx=5)
        
        # Add Monospace Toggle Checkbox
        self.mono_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(button_container, text="Force Monospace (Max Width)", variable=self.mono_var).pack(side=tk.RIGHT, padx=15)
        
        self.font_label = ttk.Label(self.root, text="No font selected", font=("Arial", 10), foreground="gray")
        self.font_label.pack(pady=5)
        
        button_frame = ttk.Frame(self.root)
        button_frame.pack(pady=10)
        
        self.choose_font_btn = ttk.Button(button_frame, text="Choose TTF Font", command=self.choose_font, width=20)
        self.choose_font_btn.pack(side=tk.LEFT, padx=10)
        
        self.export_btn = ttk.Button(button_frame, text="Export GUIF", command=self.export_guif, width=20, state=tk.DISABLED)
        self.export_btn.pack(side=tk.LEFT, padx=10)
        
        self.status_label = ttk.Label(self.root, text="", font=("Arial", 9), foreground="black", wraplength=550)
        self.status_label.pack(pady=5)
        
    def toggle_all_sizes(self, state):
        """Select or deselect all size checkboxes"""
        for var in self.size_vars.values():
            var.set(state)
    
    def choose_font(self):
        font_path = filedialog.askopenfilename(
            title="Select Font",
            filetypes=[("TrueType font", "*.ttf")]
        )
        if font_path:
            self.font_path = font_path
            self.font_label.config(text=f"Font: {Path(font_path).name}", foreground="green")
            self.export_btn.config(state=tk.NORMAL)
    
    def export_guif(self):
        if not self.font_path:
            messagebox.showerror("Error", "Please choose a font first!")
            return
        
        chars_input = self.text_box.get("1.0", tk.END).strip()
        if not chars_input:
            messagebox.showerror("Error", "Please enter characters to include!")
            return
        
        # Parse sizes from checkboxes
        sizes = [size for size, var in self.size_vars.items() if var.get()]
        if not sizes:
            messagebox.showerror("Error", "Please select at least one font size!")
            return
        
        sizes.sort()  # Ensure they're in order
        is_mono = self.mono_var.get()
        
        self.status_label.config(text="Processing...", foreground="orange")
        self.root.update()
        
        try:
            chars_to_use = chars_input.replace("\n", "")
            chars = list(dict.fromkeys(chars_to_use))
            if '?' not in chars:
                chars.append('?')
            
            save_path = filedialog.asksaveasfilename(
                title="Save GuilFont File",
                initialfile=f"{Path(self.font_path).stem}.guif",
                defaultextension=".guif",
                filetypes=[("GuilFont File", "*.guif")]
            )
            
            if not save_path:
                self.status_label.config(text="", foreground="black")
                return
            
            with zipfile.ZipFile(save_path, 'w', zipfile.ZIP_DEFLATED) as z:
                for size in sizes:
                    self.status_label.config(text=f"Generating atlas at {size}px...", foreground="orange")
                    self.root.update()
                    
                    atlas, chars_data, actual_height = generate_atlas_at_size(self.font_path, size, chars, monospace=is_mono)
                    
                    if atlas is None:
                        continue
                    
                    atlas_bytes = io.BytesIO()
                    pg.image.save(atlas, atlas_bytes, 'PNG')
                    z.writestr(f'atlas_{size}', atlas_bytes.getvalue())
                    z.writestr(f'chars_data_{size}', chars_data)
                
                metadata = f"sizes: {','.join(map(str, sizes))}"
                z.writestr('metadata', metadata)
            
            self.status_label.config(text=f"Exported successfully with {len(sizes)} sizes!", foreground="green")
            messagebox.showinfo("Success", f"Font exported to:\n{save_path}\n\nGenerated {len(sizes)} atlas sizes: {', '.join(map(str, sizes))}px")
        
        except Exception as e:
            self.status_label.config(text="Export failed!", foreground="red")
            messagebox.showerror("Error", f"Export failed:\n{str(e)}")
    
    def run(self):
        self.root.mainloop()

if __name__ == "__main__":
    app = GuilFontPackerUI()
    app.run()
